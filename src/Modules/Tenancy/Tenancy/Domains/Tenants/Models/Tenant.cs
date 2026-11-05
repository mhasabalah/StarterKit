using System.Text.RegularExpressions;

namespace Tenancy.Domains.Tenants.Models;

/// <summary>
/// The tenant (a customer company). Control-plane aggregate stored in the host database; NOT tenant-owned itself.
/// <list type="bullet">
/// <item><see cref="Identifier"/>: immutable, DNS-safe slug used as subdomain (<c>{identifier}.{RootDomain}</c>).</item>
/// <item>Lifecycle: Provisioning → Active ⇄ Suspended → PendingDeletion → Deleted (ProvisioningFailed can retry).</item>
/// <item><see cref="ConnectionSecretRef"/>: for dedicated tenants, the NAME of a configured connection string -
/// secrets never live in the database.</item>
/// </list>
/// Every state change raises <see cref="TenantChangedEvent"/> so tenant caches are dropped after the commit.
/// </summary>
public partial class Tenant : Aggregate<Guid>
{
    public const int IdentifierMinLength = 3;
    public const int IdentifierMaxLength = 40;
    public const int NameMaxLength = 200;
    public const int ReasonMaxLength = 500;
    public const int MaxCustomDomains = 5;

    /// <summary>Identifiers that would collide with platform hosts/routes.</summary>
    public static readonly IReadOnlySet<string> ReservedIdentifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "www", "api", "app", "admin", "host", "status", "mail", "smtp", "static", "cdn", "assets", "auth", "login",
        "signup", "billing", "support", "help", "docs", "dashboard", "system", "root", "tenant", "tenants", "default"
    };

    private readonly List<TenantDomain> _domains = [];

    public string Identifier { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public string? LegalName { get; private set; }
    public TenantStatus Status { get; private set; }
    public TenantIsolation Isolation { get; private set; }
    public string? ConnectionSecretRef { get; private set; }
    public string? Region { get; private set; }
    public string? Culture { get; private set; }
    public string? TimeZone { get; private set; }
    public string? Currency { get; private set; }
    public Guid? LogoAttachmentId { get; private set; }
    public string? PrimaryColor { get; private set; }
    public Guid OwnerUserId { get; private set; }
    public string? StatusReason { get; private set; }
    public DateTime? DeletionRequestedAt { get; private set; }
    public DateTime? PurgeAfter { get; private set; }

    /// <summary>While <see cref="TenantStatus.Migrating"/>: where the data is going.</summary>
    public TenantIsolation? MoveTargetIsolation { get; private set; }

    public string? MoveTargetConnectionSecretRef { get; private set; }

    /// <summary>While <see cref="TenantStatus.Migrating"/>: the status restored when the move ends.</summary>
    public TenantStatus? StatusBeforeMove { get; private set; }

    /// <summary>The lifecycle status ignoring an in-progress move (what the tenant returns to afterwards).</summary>
    public TenantStatus EffectiveStatus => Status == TenantStatus.Migrating && StatusBeforeMove is { } before
        ? before
        : Status;

    public IReadOnlyList<TenantDomain> Domains => _domains.AsReadOnly();

    private Tenant()
    {
    }

    public static Tenant Create(
        string identifier,
        string name,
        Guid ownerUserId,
        TenantIsolation isolation,
        string? connectionSecretRef = null,
        string? region = null,
        string? culture = null,
        string? timeZone = null,
        Guid? id = null)
    {
        var normalized = NormalizeIdentifier(identifier);
        if (!IsValidIdentifier(normalized))
            throw new InvalidTenantException($"'{identifier}' is not a valid tenant identifier " +
                                             $"({IdentifierMinLength}-{IdentifierMaxLength} chars: a-z, 0-9, '-').");
        if (ReservedIdentifiers.Contains(normalized) && id != TenantDefaults.DefaultTenantId)
            throw new InvalidTenantException($"'{normalized}' is reserved.");
        if (ownerUserId == Guid.Empty && id != TenantDefaults.DefaultTenantId)
            throw new InvalidTenantException("A tenant needs an owner.");

        var tenant = new Tenant
        {
            Id = id ?? Guid.CreateVersion7(),
            Identifier = normalized,
            Name = RequireName(name),
            OwnerUserId = ownerUserId,
            Isolation = isolation,
            ConnectionSecretRef = string.IsNullOrWhiteSpace(connectionSecretRef) ? null : connectionSecretRef.Trim(),
            Region = Trim(region),
            Culture = Trim(culture),
            TimeZone = Trim(timeZone),
            // Shared tenants serve immediately (seeding runs in the background); dedicated ones wait for their database.
            Status = isolation == TenantIsolation.Dedicated ? TenantStatus.Provisioning : TenantStatus.Active
        };

        tenant.AddDomainEvent(new TenantCreatedEvent(tenant.Id, tenant.Identifier, ownerUserId, isolation));
        tenant.AddDomainEvent(new TenantChangedEvent(tenant.Id));
        return tenant;
    }

    public static string NormalizeIdentifier(string identifier) => (identifier ?? string.Empty).Trim().ToLowerInvariant();

    public static bool IsValidIdentifier(string identifier) =>
        identifier.Length is >= IdentifierMinLength and <= IdentifierMaxLength && IdentifierRegex().IsMatch(identifier);

    public void UpdateProfile(string name, string? legalName, string? culture, string? timeZone, string? currency,
        Guid? logoAttachmentId, string? primaryColor)
    {
        EnsureNotDeleted();
        Name = RequireName(name);
        LegalName = Trim(legalName);
        Culture = Trim(culture);
        TimeZone = Trim(timeZone);
        Currency = Trim(currency)?.ToUpperInvariant();
        LogoAttachmentId = logoAttachmentId;
        PrimaryColor = Trim(primaryColor);
        Changed();
    }

    public void MarkProvisioned()
    {
        if (Status is not (TenantStatus.Provisioning or TenantStatus.ProvisioningFailed))
            return;
        Status = TenantStatus.Active;
        StatusReason = null;
        AddDomainEvent(new TenantActivatedEvent(Id));
        Changed();
    }

    public void MarkProvisioningFailed(string reason)
    {
        if (Status != TenantStatus.Provisioning)
            return;
        Status = TenantStatus.ProvisioningFailed;
        StatusReason = Truncate(reason);
        Changed();
    }

    public void Suspend(string reason)
    {
        if (Status == TenantStatus.Suspended)
            return;
        if (Status == TenantStatus.Migrating && StatusBeforeMove == TenantStatus.Active)
        {
            // Applied when the move ends (the tenant is read-only meanwhile anyway).
            StatusBeforeMove = TenantStatus.Suspended;
            StatusReason = Truncate(string.IsNullOrWhiteSpace(reason) ? "Suspended" : reason);
            AddDomainEvent(new TenantSuspendedEvent(Id, StatusReason!));
            Changed();
            return;
        }

        if (Status != TenantStatus.Active)
            throw new InvalidTenantException($"Only active tenants can be suspended (status: {Status}).");
        if (Id == TenantDefaults.DefaultTenantId)
            throw new InvalidTenantException("The Default tenant cannot be suspended.");

        Status = TenantStatus.Suspended;
        StatusReason = Truncate(string.IsNullOrWhiteSpace(reason) ? "Suspended" : reason);
        AddDomainEvent(new TenantSuspendedEvent(Id, StatusReason!));
        Changed();
    }

    public void Reactivate()
    {
        if (Status == TenantStatus.Active)
            return;
        if (Status == TenantStatus.Migrating && StatusBeforeMove == TenantStatus.Suspended)
        {
            StatusBeforeMove = TenantStatus.Active;
            StatusReason = null;
            AddDomainEvent(new TenantReactivatedEvent(Id));
            Changed();
            return;
        }

        if (Status != TenantStatus.Suspended)
            throw new InvalidTenantException($"Only suspended tenants can be reactivated (status: {Status}).");

        Status = TenantStatus.Active;
        StatusReason = null;
        AddDomainEvent(new TenantReactivatedEvent(Id));
        Changed();
    }

    public void RequestDeletion(DateTime utcNow, int gracePeriodDays, string? reason)
    {
        if (Id == TenantDefaults.DefaultTenantId)
            throw new InvalidTenantException("The Default tenant cannot be deleted.");
        if (Status == TenantStatus.Migrating)
            throw new InvalidTenantException("The tenant's data is being moved; try again when the move has finished.");
        if (Status is TenantStatus.PendingDeletion or TenantStatus.Deleted)
            return;

        Status = TenantStatus.PendingDeletion;
        StatusReason = Truncate(reason);
        DeletionRequestedAt = utcNow;
        PurgeAfter = utcNow.AddDays(Math.Max(0, gracePeriodDays));
        AddDomainEvent(new TenantDeletionRequestedEvent(Id, PurgeAfter.Value));
        Changed();
    }

    public void CancelDeletion()
    {
        if (Status != TenantStatus.PendingDeletion)
            throw new InvalidTenantException("The tenant is not pending deletion.");

        Status = TenantStatus.Suspended; // a platform admin reactivates explicitly after review
        StatusReason = "Deletion cancelled";
        DeletionRequestedAt = null;
        PurgeAfter = null;
        Changed();
    }

    /// <summary>Grace period over: asks every module to erase the tenant's data (idempotent handlers).</summary>
    public void RequestDataPurge()
    {
        if (Status != TenantStatus.PendingDeletion)
            throw new InvalidTenantException("Only tenants pending deletion can be purged.");
        AddDomainEvent(new TenantDataPurgeRequestedEvent(Id, Identifier, Isolation));
    }

    public void MarkDeleted()
    {
        Status = TenantStatus.Deleted;
        _domains.Clear();
        Changed();
    }

    /// <summary>
    /// Starts an automated data move to another database (<c>MoveTenantJob</c>): the tenant becomes
    /// <see cref="TenantStatus.Migrating"/> (read-only) until <see cref="CompleteMove"/> / <see cref="FailMove"/>.
    /// </summary>
    public void BeginMove(TenantIsolation isolation, string? connectionSecretRef)
    {
        if (Status is not (TenantStatus.Active or TenantStatus.Suspended))
            throw new InvalidTenantException($"Only active or suspended tenants can be moved (status: {Status}).");

        var targetRef = isolation == TenantIsolation.Dedicated && !string.IsNullOrWhiteSpace(connectionSecretRef)
            ? connectionSecretRef.Trim()
            : null;
        if (isolation == Isolation && targetRef == ConnectionSecretRef)
            throw new InvalidTenantException("The tenant's data already lives there.");

        MoveTargetIsolation = isolation;
        MoveTargetConnectionSecretRef = targetRef;
        StatusBeforeMove = Status;
        Status = TenantStatus.Migrating;
        Changed();
    }

    /// <summary>The tenant as it will be after the move (resolves the TARGET database).</summary>
    public TenantInfo ToMoveTargetInfo() => ToInfo() with
    {
        Isolation = MoveTargetIsolation ?? throw new InvalidTenantException("No move in progress."),
        ConnectionSecretRef = MoveTargetConnectionSecretRef
    };

    /// <summary>Data copied and verified: the tenant now lives in the target database.</summary>
    public void CompleteMove()
    {
        EnsureMoving();
        Isolation = MoveTargetIsolation!.Value;
        ConnectionSecretRef = MoveTargetConnectionSecretRef;
        EndMove();
    }

    /// <summary>The move failed: the tenant stays where it was.</summary>
    public void FailMove()
    {
        EnsureMoving();
        EndMove();
    }

    private void EnsureMoving()
    {
        if (Status != TenantStatus.Migrating || MoveTargetIsolation is null)
            throw new InvalidTenantException("No move in progress.");
    }

    private void EndMove()
    {
        Status = StatusBeforeMove ?? TenantStatus.Active;
        StatusBeforeMove = null;
        MoveTargetIsolation = null;
        MoveTargetConnectionSecretRef = null;
        Changed();
    }

    /// <summary>Moves a tenant between isolation levels (data migration is an operational step - see wiki).</summary>
    public void ChangeIsolation(TenantIsolation isolation, string? connectionSecretRef)
    {
        EnsureNotDeleted();
        if (Status == TenantStatus.Migrating)
            throw new InvalidTenantException("A move is in progress.");
        Isolation = isolation;
        ConnectionSecretRef = isolation == TenantIsolation.Dedicated && !string.IsNullOrWhiteSpace(connectionSecretRef)
            ? connectionSecretRef.Trim()
            : null;
        Changed();
    }

    public void TransferOwnership(Guid newOwnerUserId)
    {
        if (newOwnerUserId == Guid.Empty)
            throw new InvalidTenantException("A tenant needs an owner.");
        OwnerUserId = newOwnerUserId;
        Changed();
    }

    // ----- custom domains -----

    public TenantDomain AddDomain(string host)
    {
        EnsureNotDeleted();
        var normalized = TenantDomain.NormalizeHost(host);
        if (_domains.Any(d => d.Host == normalized))
            throw new InvalidTenantException($"'{normalized}' is already registered for this tenant.");
        if (_domains.Count >= MaxCustomDomains)
            throw new InvalidTenantException($"A tenant can have at most {MaxCustomDomains} custom domains.");

        var domain = TenantDomain.Create(normalized);
        _domains.Add(domain);
        Changed();
        return domain;
    }

    public void VerifyDomain(string host, DateTime utcNow)
    {
        var domain = FindDomain(host);
        domain.MarkVerified(utcNow);
        Changed();
    }

    public void RemoveDomain(string host)
    {
        _domains.Remove(FindDomain(host));
        Changed();
    }

    public TenantInfo ToInfo() =>
        new(Id, Identifier, Name, Status, Isolation, ConnectionSecretRef, Region, Culture, TimeZone);

    private TenantDomain FindDomain(string host)
    {
        var normalized = TenantDomain.NormalizeHost(host);
        return _domains.FirstOrDefault(d => d.Host == normalized)
               ?? throw new NotFoundException($"Domain '{normalized}' is not registered for this tenant.");
    }

    private void EnsureNotDeleted()
    {
        if (Status == TenantStatus.Deleted)
            throw new InvalidTenantException("The tenant was deleted.");
    }

    private void Changed() => AddDomainEvent(new TenantChangedEvent(Id));

    private static string RequireName(string name)
    {
        var trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length is 0 or > NameMaxLength)
            throw new InvalidTenantException($"Tenant name is required (max {NameMaxLength} characters).");
        return trimmed;
    }

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? Truncate(string? value) =>
        value is null ? null : value.Length <= ReasonMaxLength ? value : value[..ReasonMaxLength];

    [GeneratedRegex("^[a-z0-9](?:[a-z0-9-]*[a-z0-9])?$")]
    private static partial Regex IdentifierRegex();
}

/// <summary>Custom domain of a tenant; resolves the tenant only once DNS ownership is verified (TXT record).</summary>
public class TenantDomain
{
    public const int HostMaxLength = 253;

    public Guid Id { get; private set; }
    public string Host { get; private set; } = default!;
    public string VerificationToken { get; private set; } = default!;
    public DateTime? VerifiedAt { get; private set; }

    public bool IsVerified => VerifiedAt is not null;

    /// <summary>DNS TXT record name the owner must create: <c>_tenant-verify.{host}</c> with the verification token as value.</summary>
    public string VerificationRecordName => $"_tenant-verify.{Host}";

    private TenantDomain()
    {
    }

    internal static TenantDomain Create(string normalizedHost) => new()
    {
        Id = Guid.CreateVersion7(),
        Host = normalizedHost,
        VerificationToken = Convert.ToHexStringLower(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16))
    };

    internal void MarkVerified(DateTime utcNow) => VerifiedAt ??= utcNow;

    public static string NormalizeHost(string host)
    {
        var normalized = (host ?? string.Empty).Trim().TrimEnd('.').ToLowerInvariant();
        if (normalized.Length is 0 or > HostMaxLength || !normalized.Contains('.') ||
            Uri.CheckHostName(normalized) != UriHostNameType.Dns)
        {
            throw new InvalidTenantException($"'{host}' is not a valid host name.");
        }

        return normalized;
    }
}
