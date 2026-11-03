using System.Security.Cryptography;

namespace Shared.Core.MultiTenancy;

/// <summary>
/// Deterministic ids for per-tenant seed data. The Default tenant keeps the well-known seed ids (environments and
/// tests agree); every other tenant gets a stable id derived from (tenant id, seed id), so seeding many tenants
/// into one shared table never collides and re-seeding stays idempotent.
/// </summary>
public static class TenantSeedIds
{
    public static Guid For(Guid tenantId, Guid seedId)
    {
        if (tenantId == TenantDefaults.DefaultTenantId) return seedId;

        Span<byte> input = stackalloc byte[32];
        tenantId.TryWriteBytes(input[..16]);
        seedId.TryWriteBytes(input[16..]);

        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(input, hash);

        var bytes = hash[..16].ToArray();
        bytes[7] = (byte)((bytes[7] & 0x0F) | 0x50); // version 5-style (name-based)
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80); // RFC 4122 variant
        return new Guid(bytes);
    }
}
