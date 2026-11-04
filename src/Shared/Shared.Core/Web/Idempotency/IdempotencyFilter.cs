using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using Shared.Core.Exceptions;
using Shared.Core.MultiTenancy;

namespace Shared.Core.Web.Idempotency;

/// <summary>Marks an endpoint as idempotency-aware (read by <see cref="IdempotencyBodyFingerprintMiddleware"/>).</summary>
public sealed class IdempotencyMetadata;

public static class IdempotencyExtensions
{
    public const string KeyHeader = "Idempotency-Key";
    public const string ReplayedHeader = "Idempotency-Replayed";
    public const string InProgressCode = "idempotency_in_progress";
    public const string KeyReusedCode = "idempotency_key_reused";

    /// <summary>
    /// Opt-in idempotency for a POST/PUT/PATCH endpoint. With an <c>Idempotency-Key</c> header the first request runs;
    /// a duplicate while it runs gets 409, a duplicate afterwards gets the stored 2xx/4xx response with
    /// <c>Idempotency-Replayed: true</c>, and the same key with a different body gets 422. Without the header the
    /// endpoint behaves as before. Scope: tenant + user + method + route template + key.
    /// </summary>
    public static TBuilder WithIdempotency<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder
    {
        builder.WithMetadata(new IdempotencyMetadata());
        builder.AddEndpointFilter<TBuilder, IdempotencyFilter>();
        return builder;
    }
}

/// <summary>
/// Request bodies are bound before endpoint filters run, so the body fingerprint is computed here, ahead of binding.
/// Multipart bodies are hashed part by part (field names, file names, content) because the boundary changes on every
/// retry. Only runs for <see cref="IdempotencyMetadata"/> endpoints that carry the header.
/// </summary>
public sealed class IdempotencyBodyFingerprintMiddleware(RequestDelegate next)
{
    public const string FingerprintItemKey = "__IdempotencyFingerprint";

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.GetEndpoint()?.Metadata.GetMetadata<IdempotencyMetadata>() is not null &&
            context.Request.Headers.ContainsKey(IdempotencyExtensions.KeyHeader))
        {
            context.Request.EnableBuffering();
            context.Items[FingerprintItemKey] = await ComputeAsync(context.Request, context.RequestAborted);
            context.Request.Body.Position = 0;
        }

        await next(context);
    }

    private static async Task<string> ComputeAsync(HttpRequest request, CancellationToken ct)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var boundary = TryGetBoundary(request.ContentType);
        if (boundary is null)
        {
            await HashStreamAsync(hash, request.Body, ct);
        }
        else
        {
            var reader = new MultipartReader(boundary, request.Body);
            while (await reader.ReadNextSectionAsync(ct) is { } section)
            {
                hash.AppendData(Encoding.UTF8.GetBytes(section.ContentDisposition + "\n"));
                await HashStreamAsync(hash, section.Body, ct);
            }
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static string? TryGetBoundary(string? contentType)
    {
        if (contentType is null || !MediaTypeHeaderValue.TryParse(contentType, out var media) ||
            media.MediaType.Value?.Equals("multipart/form-data", StringComparison.OrdinalIgnoreCase) != true)
            return null;
        var boundary = HeaderUtilities.RemoveQuotes(media.Boundary).Value;
        return string.IsNullOrEmpty(boundary) ? null : boundary;
    }

    private static async Task HashStreamAsync(IncrementalHash hash, Stream stream, CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];
        int read;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
            hash.AppendData(buffer, 0, read);
    }
}

public sealed class IdempotencyFilter(
    IIdempotencyStore store,
    IOptions<IdempotencyOptions> options,
    ILogger<IdempotencyFilter> logger) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var settings = options.Value;

        if (!http.Request.Headers.TryGetValue(IdempotencyExtensions.KeyHeader, out var headerValues) ||
            string.IsNullOrWhiteSpace(headerValues.ToString()))
            return await next(context);

        var clientKey = headerValues.ToString().Trim();
        if (clientKey.Length > settings.MaxKeyLength)
            throw new BadRequestException(
                $"{IdempotencyExtensions.KeyHeader} must be at most {settings.MaxKeyLength} characters.");

        var storeKey = BuildStoreKey(http, clientKey);
        var fingerprint = http.Items[IdempotencyBodyFingerprintMiddleware.FingerprintItemKey] as string ?? string.Empty;

        IdempotencyReservation reservation;
        try
        {
            reservation = await store.TryReserveAsync(storeKey, fingerprint, settings.InFlightTtl, http.RequestAborted);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Idempotency store unavailable; processing {Method} {Path} without idempotency",
                http.Request.Method, http.Request.Path.Value);
            return await next(context);
        }

        switch (reservation.Status)
        {
            case IdempotencyReservationStatus.InProgress:
                throw new ConflictException("A request with this Idempotency-Key is still being processed.")
                {
                    ErrorCode = IdempotencyExtensions.InProgressCode
                };
            case IdempotencyReservationStatus.FingerprintMismatch:
                throw new CustomException("This Idempotency-Key was already used with a different request body.",
                    HttpStatusCode.UnprocessableEntity)
                {
                    ErrorCode = IdempotencyExtensions.KeyReusedCode
                };
            case IdempotencyReservationStatus.Completed:
                return StoredResponseResult.From(reservation.Entry!, replayed: true);
        }

        // Reserved: this request owns the key (via its owner token) and must complete or release it.
        var owner = reservation.Owner ?? string.Empty;
        StoredResponseResult captured;
        try
        {
            captured = await CaptureAsync(http, await next(context));
        }
        catch
        {
            await ReleaseAsync(storeKey, owner);
            throw;
        }

        var status = captured.StatusCode;
        if ((status is >= 200 and < 300 or >= 400 and < 500) &&
            (captured.Body?.Length ?? 0) <= settings.MaxStoredBodyBytes)
        {
            try
            {
                if (!await store.CompleteAsync(storeKey, owner, captured.ToEntry(fingerprint), settings.Ttl, CancellationToken.None))
                    logger.LogWarning("Idempotency reservation expired and was taken over; the response was not stored");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not store the idempotent response; the key is released");
                await ReleaseAsync(storeKey, owner);
            }
        }
        else
        {
            await ReleaseAsync(storeKey, owner);
        }

        return captured;
    }

    private async Task ReleaseAsync(string storeKey, string owner)
    {
        try
        {
            await store.ReleaseAsync(storeKey, owner, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not release idempotency key (it expires on its own)");
        }
    }

    /// <summary>Runs the endpoint's result against a buffer so status, headers and body can be stored.</summary>
    private static async Task<StoredResponseResult> CaptureAsync(HttpContext http, object? value)
    {
        var result = value as IResult ?? Results.Ok(value);
        var original = http.Response.Body;
        await using var buffer = new MemoryStream();
        http.Response.Body = buffer;
        try
        {
            await result.ExecuteAsync(http);
        }
        finally
        {
            http.Response.Body = original;
        }

        var location = http.Response.Headers.Location.ToString();
        return new StoredResponseResult(
            http.Response.StatusCode,
            http.Response.ContentType,
            string.IsNullOrEmpty(location) ? null : location,
            buffer.ToArray(),
            Replayed: false);
    }

    /// <summary>Key = SHA-256 over tenant | user | METHOD route-template | client key.</summary>
    private static string BuildStoreKey(HttpContext http, string clientKey)
    {
        var tenant = http.RequestServices.GetService<ITenantContext>()?.TenantId?.ToString("N") ?? "-";
        var user = http.RequestServices.GetService<IHttpContextService>()?.GetUserId() ?? "anonymous";
        var route = (http.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? http.Request.Path.Value ?? string.Empty;
        var scope = $"{tenant}|{user}|{http.Request.Method} {route}|{clientKey}";
        return "idempotency:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(scope)));
    }
}

/// <summary>Writes a captured or replayed response verbatim.</summary>
public sealed record StoredResponseResult(
    int StatusCode, string? ContentType, string? Location, byte[]? Body, bool Replayed) : IResult
{
    public static StoredResponseResult From(IdempotencyEntry entry, bool replayed) =>
        new(entry.StatusCode, entry.ContentType, entry.Location, entry.Body, replayed);

    public IdempotencyEntry ToEntry(string fingerprint) =>
        new(true, fingerprint, StatusCode, ContentType, Location, Body);

    public async Task ExecuteAsync(HttpContext httpContext)
    {
        var response = httpContext.Response;
        response.StatusCode = StatusCode;
        if (ContentType is not null)
            response.ContentType = ContentType;
        if (Location is not null)
            response.Headers.Location = Location;
        if (Replayed)
            response.Headers[IdempotencyExtensions.ReplayedHeader] = "true";
        if (Body is { Length: > 0 })
            await response.Body.WriteAsync(Body, httpContext.RequestAborted);
    }
}
