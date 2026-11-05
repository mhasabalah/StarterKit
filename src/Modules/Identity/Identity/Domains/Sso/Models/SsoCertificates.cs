using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Identity.Contracts.Sso;
using Identity.Domains.Sso.Exceptions;

namespace Identity.Domains.Sso.Models;

/// <summary>Parses IdP signing certificates given as PEM or Base64 DER (public certificates only).</summary>
public static class SsoCertificates
{
    public static string ToBase64Der(string value)
    {
        try
        {
            using var certificate = Load(value);
            return Convert.ToBase64String(certificate.RawData);
        }
        catch (CryptographicException)
        {
            throw new InvalidSsoConnectionException("Sso.Connection.InvalidCertificate");
        }
        catch (FormatException)
        {
            throw new InvalidSsoConnectionException("Sso.Connection.InvalidCertificate");
        }
    }

    public static X509Certificate2 Load(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Contains("-----BEGIN", StringComparison.Ordinal)
            ? X509Certificate2.CreateFromPem(trimmed)
            : X509CertificateLoader.LoadCertificate(Convert.FromBase64String(trimmed));
    }

    public static SsoCertificateDto Describe(string base64Der)
    {
        using var certificate = X509CertificateLoader.LoadCertificate(Convert.FromBase64String(base64Der));
        return new SsoCertificateDto(certificate.Subject, certificate.Thumbprint, certificate.NotAfter.ToUniversalTime());
    }
}
