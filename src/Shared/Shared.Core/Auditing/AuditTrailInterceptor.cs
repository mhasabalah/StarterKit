using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Options;
using Shared.Core.Data.Interceptors;
using Shared.Core.MultiTenancy;
using Shared.Core.Web;

namespace Shared.Core.Auditing;

/// <summary>
/// Writes one <see cref="AuditEntryBase"/> row per changed <see cref="IAuditable"/> entity, in the SAME SaveChanges
/// (same database transaction) as the change: the row exists if and only if the change was committed.
/// <list type="bullet">
/// <item>Runs AFTER <c>TenantEntityInterceptor</c> (tenant stamped and validated) and BEFORE the outbox.</item>
/// <item>Diff: Added = new values, Modified = old -> new of the changed properties only, Deleted = old values. Owned
/// values (<c>Name</c>, <c>Pricing</c>) are flattened as <c>Name.En</c>. Bookkeeping columns (Version, Created*,
/// LastModified*, TenantId), primary keys, <c>[AuditIgnore]</c> properties and any property whose name contains
/// Password / Secret / Token / Hash are never recorded. Long strings are cut and the whole diff is capped.</item>
/// <item>A Modified entity whose only changes were skipped properties produces no row.</item>
/// <item>Actor = current user, impersonator = <c>act</c> claim, plus correlation id and client IP.</item>
/// <item>Append-only: modifying or deleting an audit row through the change tracker throws, except in host scope
/// (tenant purge). The retention job uses a set-based delete.</item>
/// </list>
/// </summary>
public sealed class AuditTrailInterceptor(
    IHttpContextService httpContext,
    ITenantContext tenantContext,
    TimeProvider timeProvider,
    IOptions<AuditTrailOptions> options) : SaveChangesInterceptor
{
    private static readonly string[] SecretNameParts = ["Password", "Secret", "Token", "Hash"];

    private static readonly HashSet<string> BookkeepingProperties = new(StringComparer.Ordinal)
    {
        nameof(IEntity.Version), nameof(IEntity.CreatedAt), nameof(IEntity.CreatedBy),
        nameof(IEntity.LastModified), nameof(IEntity.LastModifiedBy), nameof(IMustHaveTenant.TenantId)
    };

    private static readonly ConcurrentDictionary<IProperty, bool> SkippedCache = new();

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Write(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Write(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Write(DbContext? context)
    {
        if (context is null) return;

        EnsureAppendOnly(context);

        var auditables = context.ChangeTracker.Entries<IAuditable>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted ||
                        (e.State == EntityState.Unchanged && e.HasChangedOwnedEntities()))
            .ToList();
        if (auditables.Count == 0) return;

        var entryType = context.FindAuditEntryType()
                        ?? throw new InvalidOperationException(
                            $"{context.GetType().Name} saves IAuditable entities " +
                            $"({string.Join(", ", auditables.Select(a => a.Metadata.ClrType.Name).Distinct())}) " +
                            "but maps no audit table. Call builder.AddAuditTrail() (host context) or " +
                            "builder.AddTenantAuditTrail() (tenant-data context) in OnModelCreating and add a migration.");

        var settings = options.Value;
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var actor = httpContext.GetUserIdAsGuid();
        var impersonator = Guid.TryParse(httpContext.GetClaimValue(TenantClaimTypes.Actor), out var act)
            ? act
            : (Guid?)null;
        var correlationId = Truncate(httpContext.GetCorrelationId(), AuditEntryBase.CorrelationIdMaxLength);
        var ip = Truncate(httpContext.GetIpAddress(), AuditEntryBase.IpAddressMaxLength);

        foreach (var entry in auditables)
        {
            var action = entry.State switch
            {
                EntityState.Added => AuditAction.Added,
                EntityState.Deleted => AuditAction.Deleted,
                _ => AuditAction.Modified
            };

            var changes = CollectChanges(entry, action, settings);
            if (action == AuditAction.Modified && changes.Count == 0) continue;

            AuditEntryBase row;
            if (entryType == typeof(TenantAuditEntry))
            {
                var tenantId = entry.Entity is IMustHaveTenant { TenantId: var owned } && owned != Guid.Empty
                    ? owned
                    : tenantContext.TenantId ?? throw new TenantNotResolvedException(
                        $"cannot audit {entry.Metadata.ClrType.Name} without a tenant.");
                row = new TenantAuditEntry { TenantId = tenantId };
            }
            else
            {
                row = new AuditEntry { TenantId = tenantContext.TenantId };
            }

            row.Id = Guid.CreateVersion7();
            row.EntityType = Truncate(entry.Metadata.ClrType.Name, AuditEntryBase.EntityTypeMaxLength)!;
            row.EntityId = Truncate(KeyOf(entry), AuditEntryBase.EntityIdMaxLength)!;
            row.Action = action;
            row.ChangesJson = Serialize(changes, settings);
            row.ActorUserId = actor;
            row.ImpersonatorUserId = impersonator;
            row.CorrelationId = correlationId;
            row.IpAddress = ip;
            row.OccurredAt = now;
            context.Add((object)row);
        }
    }

    private void EnsureAppendOnly(DbContext context)
    {
        if (tenantContext.IsHostScope) return;

        var offender = context.ChangeTracker.Entries<AuditEntryBase>()
            .FirstOrDefault(e => e.State is EntityState.Modified or EntityState.Deleted);
        if (offender is not null)
            throw new InvalidOperationException("The audit trail is append-only.");
    }

    private static Dictionary<string, Dictionary<string, object?>> CollectChanges(
        EntityEntry entry, AuditAction action, AuditTrailOptions settings)
    {
        var changes = new Dictionary<string, Dictionary<string, object?>>(StringComparer.Ordinal);
        Collect(entry, prefix: "", owned: false, action, settings, changes);

        foreach (var reference in entry.References)
        {
            var target = reference.TargetEntry;
            if (target is null || !target.Metadata.IsOwned()) continue;
            if (target.State == EntityState.Unchanged && action == AuditAction.Modified) continue;

            // A replaced owned value is tracked as Deleted(old) + Added(new) with the same key: diff the pair.
            var replaced = action == AuditAction.Modified && target.State == EntityState.Added
                ? FindReplacedOwned(entry.Context, target)
                : null;
            Collect(target, reference.Metadata.Name + ".", owned: true, action, settings, changes, replaced);
        }

        return changes;
    }

    private static void Collect(EntityEntry entry, string prefix, bool owned, AuditAction action,
        AuditTrailOptions settings, Dictionary<string, Dictionary<string, object?>> changes,
        EntityEntry? replaced = null)
    {
        foreach (var property in entry.Properties)
        {
            var metadata = property.Metadata;
            if (IsSkipped(metadata, owned)) continue;

            var diff = new Dictionary<string, object?>(2);
            switch (action)
            {
                case AuditAction.Added:
                    diff["new"] = Normalize(property.CurrentValue, settings);
                    break;
                case AuditAction.Deleted:
                    diff["old"] = Normalize(property.OriginalValue, settings);
                    break;
                default:
                    if (entry.State == EntityState.Added)
                    {
                        // Owned value replaced. With the old instance known, only real differences are recorded.
                        if (replaced is not null)
                        {
                            var before = replaced.Property(metadata.Name).OriginalValue;
                            if (Equals(before, property.CurrentValue)) continue;
                            diff["old"] = Normalize(before, settings);
                        }

                        diff["new"] = Normalize(property.CurrentValue, settings);
                    }
                    else
                    {
                        if (!property.IsModified || Equals(property.OriginalValue, property.CurrentValue)) continue;
                        diff["old"] = Normalize(property.OriginalValue, settings);
                        diff["new"] = Normalize(property.CurrentValue, settings);
                    }

                    break;
            }

            changes[prefix + metadata.Name] = diff;
        }
    }

    private static EntityEntry? FindReplacedOwned(DbContext context, EntityEntry added)
    {
        var key = added.Metadata.FindPrimaryKey();
        if (key is null) return null;

        return context.ChangeTracker.Entries()
            .FirstOrDefault(e => e.State == EntityState.Deleted && e.Metadata == added.Metadata &&
                                 key.Properties.All(p => Equals(e.Property(p.Name).CurrentValue,
                                     added.Property(p.Name).CurrentValue)));
    }

    private static bool IsSkipped(IProperty property, bool owned) =>
        SkippedCache.GetOrAdd(property, p =>
        {
            if (p.IsPrimaryKey() || p.IsShadowProperty()) return true;
            if (owned && p.IsForeignKey()) return true;
            if (BookkeepingProperties.Contains(p.Name)) return true;
            if (SecretNameParts.Any(part => p.Name.Contains(part, StringComparison.OrdinalIgnoreCase))) return true;

            var member = (MemberInfo?)p.PropertyInfo ?? p.FieldInfo;
            return member?.GetCustomAttribute<AuditIgnoreAttribute>() is not null;
        });

    private static object? Normalize(object? value, AuditTrailOptions settings) => value switch
    {
        null => null,
        string s => s.Length <= settings.MaxValueLength ? s : s[..settings.MaxValueLength] + "...",
        Enum e => e.ToString(),
        Guid g => g.ToString(),
        DateTime d => d.ToString("o", CultureInfo.InvariantCulture),
        DateTimeOffset d => d.ToString("o", CultureInfo.InvariantCulture),
        byte[] b => $"[{b.Length} bytes]",
        bool or byte or short or int or long or float or double or decimal => value,
        _ => Normalize(Convert.ToString(value, CultureInfo.InvariantCulture), settings)
    };

    private static string Serialize(Dictionary<string, Dictionary<string, object?>> changes, AuditTrailOptions settings)
    {
        var json = JsonSerializer.Serialize(changes, Json);
        if (Encoding.UTF8.GetByteCount(json) <= settings.MaxChangesBytes) return json;

        var names = new List<string>();
        var size = 40;
        foreach (var name in changes.Keys.Order(StringComparer.Ordinal))
        {
            size += name.Length + 4;
            if (size > settings.MaxChangesBytes) break;
            names.Add(name);
        }

        return JsonSerializer.Serialize(
            new Dictionary<string, object> { ["_truncated"] = true, ["properties"] = names }, Json);
    }

    private static string KeyOf(EntityEntry entry)
    {
        var key = entry.Metadata.FindPrimaryKey();
        if (key is null) return "";
        return string.Join(',', key.Properties.Select(p =>
            Convert.ToString(entry.Property(p.Name).CurrentValue, CultureInfo.InvariantCulture)));
    }

    private static string? Truncate(string? value, int max) =>
        value is null || value.Length <= max ? value : value[..max];
}
