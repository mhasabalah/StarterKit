using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Shared.Core.Web;

/// <summary>
/// Correlation id handling: one header name (<see cref="HeaderName"/>), any reasonable value accepted
/// (not only GUIDs), echoed on the response and pushed into the logging scope as <c>CorrelationId</c>.
/// </summary>
public static class CorrelationExtensions
{
    public const string HeaderName = "X-Correlation-Id";

    /// <summary>HttpContext.Items key holding the (string) correlation id of the current request.</summary>
    public const string ItemKey = "CorrelationId";

    private const int MaxLength = 128;

    public static IApplicationBuilder UseCorrelationId(this IApplicationBuilder app)
    {
        return app.Use(async (ctx, next) =>
        {
            var correlationId = Sanitize(ctx.Request.Headers[HeaderName].FirstOrDefault())
                                ?? Guid.NewGuid().ToString("N");

            ctx.Items[ItemKey] = correlationId;

            ctx.Response.OnStarting(() =>
            {
                ctx.Response.Headers[HeaderName] = correlationId;
                return Task.CompletedTask;
            });

            var logger = ctx.RequestServices.GetService<ILoggerFactory>()?.CreateLogger("CorrelationId");
            using (logger?.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
            {
                await next();
            }
        });
    }

    /// <summary>Returns the current request's correlation id (never null inside the pipeline).</summary>
    public static string GetCorrelationId(this HttpContext context)
    {
        return context.Items.TryGetValue(ItemKey, out var value) && value is string s && s.Length > 0
            ? s
            : context.TraceIdentifier;
    }

    /// <summary>
    /// Accepts printable, header-safe tokens up to <see cref="MaxLength"/> chars. Anything else (control chars,
    /// whitespace, oversized values) is rejected so it can't be used for log injection; a new id is generated.
    /// </summary>
    public static string? Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        value = value.Trim();
        if (value.Length > MaxLength)
            return null;

        foreach (var c in value)
        {
            var ok = char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or ':' or '/' or '+' or '=';
            if (!ok)
                return null;
        }

        return value;
    }
}
