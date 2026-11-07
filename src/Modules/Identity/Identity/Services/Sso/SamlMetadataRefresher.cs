using System.Xml;
using Identity.Data.Repository.Sso;
using Identity.Domains.Sso.Models;
using Identity.Options;
using Microsoft.Extensions.Options;

namespace Identity.Services.Sso;

/// <summary>Result of refreshing one connection's IdP metadata.</summary>
public sealed record SamlMetadataRefreshResult(SamlMetadataOutcome Outcome, string? Error, DateTime? RefreshedAt);

/// <summary>
/// Downloads the IdP metadata XML of a tenant-configured URL. Outbound calls to customer-supplied URLs are an SSRF
/// boundary: only the <c>identity-sso</c> client (GuardedConnector: no private/loopback/metadata addresses, no
/// redirects, short timeout), HTTPS only (unless <c>AllowInsecureIdentityProviders</c>), response size capped, and the
/// XML is validated with DTD processing prohibited before any SAML library sees it.
/// </summary>
public sealed class SamlMetadataFetcher(
    IHttpClientFactory httpClientFactory,
    IOptions<SsoOptions> options,
    ILogger<SamlMetadataFetcher> logger)
{
    public const int MaxBytes = 1024 * 1024;

    /// <summary>The metadata XML, or the <see cref="SamlMetadataErrors"/> code explaining why there is none.</summary>
    public async Task<(string? Xml, string? Error)> FetchAsync(string? url, CancellationToken ct)
    {
        if (!SsoHttp.IsAcceptableUrl(url, options.Value.AllowInsecureIdentityProviders))
            return (null, SamlMetadataErrors.UrlNotAllowed);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Accept.ParseAdd("application/samlmetadata+xml, application/xml, text/xml");
            using var response = await httpClientFactory.CreateClient(SsoHttp.ClientName)
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("SAML metadata fetch from {Url} returned {Status}", url, (int)response.StatusCode);
                return (null, SamlMetadataErrors.FetchFailed);
            }

            if (response.Content.Headers.ContentLength > MaxBytes)
                return (null, SamlMetadataErrors.TooLarge);

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            var buffer = new MemoryStream();
            var chunk = new byte[16 * 1024];
            int read;
            while ((read = await stream.ReadAsync(chunk, ct)) > 0)
            {
                if (buffer.Length + read > MaxBytes)
                    return (null, SamlMetadataErrors.TooLarge);
                buffer.Write(chunk, 0, read);
            }

            var xml = Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
            return IsSafeXml(xml) ? (xml, null) : (null, SamlMetadataErrors.InvalidMetadata);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested &&
                                   ex is HttpRequestException or TaskCanceledException or IOException)
        {
            logger.LogWarning(ex, "SAML metadata fetch from {Url} failed", url);
            return (null, SamlMetadataErrors.FetchFailed);
        }
    }

    /// <summary>Well-formed and free of DTDs (no entity expansion / external entities).</summary>
    public static bool IsSafeXml(string xml)
    {
        try
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersFromEntities = 0,
                MaxCharactersInDocument = MaxBytes * 2L
            };
            using var reader = XmlReader.Create(new StringReader(xml), settings);
            while (reader.Read())
            {
            }

            return true;
        }
        catch (XmlException)
        {
            return false;
        }
    }
}

/// <summary>
/// Refreshes the IdP signing certificates and SSO URL of ONE SAML connection from its metadata URL. Shared by the
/// recurring <c>RefreshSamlMetadataJob</c> and the "refresh now" admin command.
/// <list type="bullet">
/// <item>Entity-id mismatch, unparseable metadata, a signature that does not verify against the pinned certificates or no
/// certificates: nothing changes; the error code is recorded. The entity id is a consistency check, NOT protection
/// against a hijacked metadata host unless the metadata is signed (<see cref="SamlMetadataSignature"/>).</item>
/// <item>Unchanged metadata: only <c>SamlMetadataRefreshedAt</c> is touched (a set-based update, no version bump, so
/// it never conflicts with an admin editing the connection).</item>
/// <item>Changed: the aggregate is updated and saved (version bump + change event).</item>
/// </list>
/// </summary>
public sealed class SamlMetadataRefresher(
    SamlMetadataFetcher fetcher,
    ISsoConnectionRepository connections,
    IOptions<SsoOptions> options,
    TimeProvider timeProvider,
    ILogger<SamlMetadataRefresher> logger)
{
    public async Task<SamlMetadataRefreshResult> RefreshAsync(SsoConnection connection, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var (xml, error) = await fetcher.FetchAsync(connection.SamlIdpMetadataUrl, ct);
        if (xml is null)
            return await FailAsync(connection, error!, ct);

        // Signed metadata must verify against the keys we already trust (rollover: the old key signs the new metadata).
        // Unsigned metadata is accepted - see SamlMetadataSignature for what that does NOT protect against.
        if (SamlMetadataSignature.Verify(xml, connection.SamlIdpCertificates) == MetadataSignature.Invalid)
            return await FailAsync(connection, SamlMetadataErrors.SignatureInvalid, ct);

        string entityId;
        string? ssoUrl;
        IReadOnlyList<string> certificates;
        try
        {
            (entityId, ssoUrl, certificates) = SamlSsoClient.ParseIdpMetadata(xml);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "SAML metadata of connection {ConnectionId} could not be parsed", connection.Id);
            return await FailAsync(connection, SamlMetadataErrors.InvalidMetadata, ct);
        }

        // The SSO URL from metadata must be acceptable too, or the connection would stop working.
        if (ssoUrl is not null && !SsoHttp.IsAcceptableUrl(ssoUrl, allowInsecure: options.Value.AllowInsecureIdentityProviders))
            ssoUrl = null;

        var applied = connection.ApplyIdpMetadata(entityId, ssoUrl, certificates, now);
        switch (applied.Outcome)
        {
            case SamlMetadataOutcome.Updated:
                await connections.SaveAsync(ct);
                logger.LogInformation("SAML metadata of connection {ConnectionId} updated", connection.Id);
                return new SamlMetadataRefreshResult(SamlMetadataOutcome.Updated, null, now);
            case SamlMetadataOutcome.Unchanged:
                await connections.RecordMetadataRefreshAsync(connection.Id, now, null, ct);
                return new SamlMetadataRefreshResult(SamlMetadataOutcome.Unchanged, null, now);
            default:
                return await FailAsync(connection, applied.Error!, ct);
        }
    }

    private async Task<SamlMetadataRefreshResult> FailAsync(
        SsoConnection connection, string error, CancellationToken ct)
    {
        logger.LogWarning("SAML metadata refresh of connection {ConnectionId} failed: {Error}", connection.Id, error);
        await connections.RecordMetadataRefreshAsync(connection.Id, null, error, ct);
        return new SamlMetadataRefreshResult(SamlMetadataOutcome.Failed, error, connection.SamlMetadataRefreshedAt);
    }
}
