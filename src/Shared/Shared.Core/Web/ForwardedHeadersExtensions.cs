using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using IPNetwork = System.Net.IPNetwork;

namespace Shared.Core.Web;

/// <summary>
/// Settings bound from the <c>ForwardedHeaders</c> configuration section.
/// <para>
/// <c>X-Forwarded-For</c> / <c>X-Forwarded-Proto</c> are honoured ONLY when the TCP peer is a trusted proxy.
/// Out of the box only loopback is trusted (ASP.NET Core default). Behind Nginx in Docker, either list the
/// proxy explicitly (<c>KnownProxies</c> / <c>KnownNetworks</c>) or set <c>TrustPrivateNetworks=true</c> so the
/// RFC 1918 / ULA ranges used by Docker networks are trusted. Never trust private ranges when the API port is
/// reachable from untrusted hosts on those ranges.
/// </para>
/// </summary>
public sealed class ForwardedHeadersSettings
{
    public const string SectionName = "ForwardedHeaders";

    /// <summary>Master switch. When false the middleware is not added at all.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Exact proxy IPs (e.g. "10.0.0.5").</summary>
    public string[] KnownProxies { get; set; } = [];

    /// <summary>Proxy networks in CIDR notation (e.g. "172.18.0.0/16").</summary>
    public string[] KnownNetworks { get; set; } = [];

    /// <summary>Adds 10.0.0.0/8, 172.16.0.0/12, 192.168.0.0/16 and fc00::/7 (Docker / private LAN proxies).</summary>
    public bool TrustPrivateNetworks { get; set; }

    /// <summary>How many proxy hops to unwrap. 1 = a single Nginx in front of the API.</summary>
    public int ForwardLimit { get; set; } = 1;

    internal static readonly string[] PrivateNetworks =
        ["10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16", "fc00::/7"];
}

public static class TrustedForwardedHeadersExtensions
{
    /// <summary>
    /// Builds <see cref="ForwardedHeadersOptions"/> from configuration. Exposed for tests.
    /// </summary>
    public static ForwardedHeadersOptions BuildForwardedHeadersOptions(IConfiguration configuration)
    {
        var settings = configuration.GetSection(ForwardedHeadersSettings.SectionName).Get<ForwardedHeadersSettings>()
                       ?? new ForwardedHeadersSettings();

        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
            ForwardLimit = settings.ForwardLimit < 1 ? 1 : settings.ForwardLimit,
            // Absent X-Forwarded-Proto must not discard a valid X-Forwarded-For.
            RequireHeaderSymmetry = false
        };

        // Defaults (loopback) are kept; configured entries are added on top.
        foreach (var proxy in settings.KnownProxies.Where(p => !string.IsNullOrWhiteSpace(p)))
        {
            if (!IPAddress.TryParse(proxy.Trim(), out var ip))
                throw new InvalidOperationException(
                    $"{ForwardedHeadersSettings.SectionName}:KnownProxies contains an invalid IP address: '{proxy}'.");
            options.KnownProxies.Add(ip);
        }

        var networks = settings.KnownNetworks.AsEnumerable();
        if (settings.TrustPrivateNetworks)
            networks = networks.Concat(ForwardedHeadersSettings.PrivateNetworks);

        foreach (var cidr in networks.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct())
        {
            if (!IPNetwork.TryParse(cidr.Trim(), out var network))
                throw new InvalidOperationException(
                    $"{ForwardedHeadersSettings.SectionName}:KnownNetworks contains an invalid CIDR: '{cidr}'.");
            options.KnownIPNetworks.Add(network);
        }

        return options;
    }

    /// <summary>
    /// The ASP.NET Core host flag <c>ASPNETCORE_FORWARDEDHEADERS_ENABLED=true</c> (config key <c>ForwardedHeaders_Enabled</c>)
    /// adds a forwarded-headers middleware that trusts EVERY peer (KnownProxies/KnownNetworks cleared), i.e. any
    /// client can spoof its IP with X-Forwarded-For. This switches it off; <see cref="UseTrustedForwardedHeaders"/>
    /// replaces it with the allow-listed version. Returns true when the flag was found and disabled.
    /// </summary>
    public static bool DisableTrustAllForwardedHeadersFlag(IConfiguration configuration)
    {
        const string legacyKey = "ForwardedHeaders_Enabled";
        if (!string.Equals(configuration[legacyKey], "true", StringComparison.OrdinalIgnoreCase))
            return false;

        configuration[legacyKey] = "false";
        return true;
    }

    /// <summary>
    /// Must be the FIRST middleware so that rate limiting, logging and <see cref="IHttpContextService.GetIpAddress"/>
    /// all see the real client IP in <c>HttpContext.Connection.RemoteIpAddress</c>.
    /// </summary>
    public static IApplicationBuilder UseTrustedForwardedHeaders(this IApplicationBuilder app, IConfiguration configuration)
    {
        var enabled = configuration.GetValue($"{ForwardedHeadersSettings.SectionName}:Enabled", true);
        if (!enabled)
            return app;

        return app.UseForwardedHeaders(BuildForwardedHeadersOptions(configuration));
    }
}
