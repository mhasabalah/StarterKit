using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Shared.Core.Net;

/// <summary>Proves control of a domain through a DNS TXT record (tenant custom domains, SSO email domains).</summary>
public interface IDnsTxtVerifier
{
    /// <summary>True when the TXT record <paramref name="recordName"/> contains <paramref name="expectedValue"/>.</summary>
    Task<bool> HasTxtRecordAsync(string recordName, string expectedValue, CancellationToken cancellationToken = default);
}

/// <summary>
/// DNS-over-HTTPS TXT lookup (JSON API, e.g. Cloudflare <c>https://cloudflare-dns.com/dns-query</c>) - no extra DNS
/// library needed. Endpoint: <c>MultiTenancy:DomainVerification:DohEndpoint</c>.
/// </summary>
public sealed class DnsOverHttpsTxtVerifier(
    HttpClient httpClient,
    IConfiguration configuration,
    ILogger<DnsOverHttpsTxtVerifier> logger) : IDnsTxtVerifier
{
    public const string DefaultEndpoint = "https://cloudflare-dns.com/dns-query";

    public async Task<bool> HasTxtRecordAsync(string recordName, string expectedValue,
        CancellationToken cancellationToken = default)
    {
        var endpoint = configuration["MultiTenancy:DomainVerification:DohEndpoint"] ?? DefaultEndpoint;
        var url = $"{endpoint}?name={Uri.EscapeDataString(recordName)}&type=TXT";

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Accept.ParseAdd("application/dns-json");
            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return false;

            var body = await response.Content.ReadFromJsonAsync<DohResponse>(cancellationToken);
            return body?.Answer?.Any(a => a.Data?.Trim('"') == expectedValue) == true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            logger.LogWarning(ex, "DNS verification of {Record} failed", recordName);
            return false;
        }
    }

    private sealed record DohResponse([property: JsonPropertyName("Answer")] List<DohAnswer>? Answer);

    private sealed record DohAnswer([property: JsonPropertyName("data")] string? Data);
}
