using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.Repositories;

namespace Identity.Infrastructure.DataProtection;

/// <summary>One ASP.NET Data Protection key (XML) in <c>identity.data_protection_keys</c>.</summary>
public class DataProtectionKeyEntry
{
    public int Id { get; set; }

    public string? FriendlyName { get; set; }

    public string Xml { get; set; } = string.Empty;
}

/// <summary>
/// Persists the Data Protection key ring in the Identity database, so every API instance shares it and it survives
/// container restarts. Without that, encrypted authenticator (TOTP) keys - and password-reset tokens - become
/// undecryptable after a redeploy and every admin would be locked out of 2FA.
/// <para>
/// The keys are stored unencrypted next to the data they protect; to also encrypt them at rest, add
/// <c>ProtectKeysWithCertificate(...)</c> (or a KMS) on the <c>IDataProtectionBuilder</c>.
/// </para>
/// </summary>
public sealed class DataProtectionKeyRepository(IServiceScopeFactory scopeFactory) : IXmlRepository
{
    public IReadOnlyCollection<XElement> GetAllElements()
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationIdentityDbContext>();

        return db.DataProtectionKeys.AsNoTracking()
            .OrderBy(k => k.Id)
            .Select(k => k.Xml)
            .AsEnumerable()
            .Select(XElement.Parse)
            .ToList()
            .AsReadOnly();
    }

    public void StoreElement(XElement element, string friendlyName)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationIdentityDbContext>();

        db.DataProtectionKeys.Add(new DataProtectionKeyEntry
        {
            FriendlyName = friendlyName,
            Xml = element.ToString(SaveOptions.DisableFormatting)
        });
        db.SaveChanges();
    }
}
