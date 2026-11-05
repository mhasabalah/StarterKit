using Microsoft.Extensions.DependencyInjection;

namespace Shared.Core.Net;

public static class DnsTxtVerifierServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IDnsTxtVerifier"/> (DNS-over-HTTPS, typed HTTP client, 10 s timeout) once. Idempotent:
    /// every module that needs domain verification (Tenancy custom domains, Identity SSO domains) calls it, and the
    /// first call wins, so the modules never fight over the client configuration.
    /// </summary>
    public static IServiceCollection AddDnsTxtVerifier(this IServiceCollection services)
    {
        if (services.Any(d => d.ServiceType == typeof(IDnsTxtVerifier)))
            return services;

        services.AddHttpClient<IDnsTxtVerifier, DnsOverHttpsTxtVerifier>(c => c.Timeout = TimeSpan.FromSeconds(10));
        return services;
    }
}
