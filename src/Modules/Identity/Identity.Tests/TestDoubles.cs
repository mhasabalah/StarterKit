using Identity.Data.Repository.Otp;
using Identity.Domains.Users.Models;
using Identity.Models;
using Identity.Services.Otp;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Identity.Tests;

public sealed class FakeTimeProvider(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = start;

    public override DateTimeOffset GetUtcNow() => Now;

    public void Advance(TimeSpan by) => Now += by;
}

/// <summary>
/// In-memory IOtpCodeRepository with the same conditional (compare-and-set) semantics as the EF implementation.
/// Every operation runs under one lock, like the single-statement UPDATEs in PostgreSQL.
/// </summary>
public sealed class InMemoryOtpCodeRepository : IOtpCodeRepository
{
    private readonly object _gate = new();

    public Dictionary<(Guid, OtpPurpose), OtpCode> Rows { get; } = new();

    public Task<OtpCode?> GetAsync(Guid userId, OtpPurpose purpose, CancellationToken ct = default)
    {
        lock (_gate)
        {
            return Task.FromResult(Rows.TryGetValue((userId, purpose), out var row) ? Clone(row) : null);
        }
    }

    public Task<bool> TryIssueAsync(
        OtpCode code, DateTime cooldownBoundary, DateTime windowBoundary, int maxSendsPerWindow,
        CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (!Rows.TryGetValue((code.UserId, code.Purpose), out var row))
            {
                Rows[(code.UserId, code.Purpose)] = Clone(code);
                return Task.FromResult(true);
            }

            var newWindow = row.WindowStartedAt <= windowBoundary;
            if (row.LastSentAt > cooldownBoundary || (!newWindow && row.SendCount >= maxSendsPerWindow))
            {
                return Task.FromResult(false);
            }

            row.CodeHash = code.CodeHash;
            row.ExpiresAt = code.ExpiresAt;
            row.Attempts = 0;
            row.MaxAttempts = code.MaxAttempts;
            row.LastSentAt = code.LastSentAt;
            row.SendCount = newWindow ? 1 : row.SendCount + 1;
            row.WindowStartedAt = newWindow ? code.WindowStartedAt : row.WindowStartedAt;
            return Task.FromResult(true);
        }
    }

    public Task<bool> TryConsumeAttemptAsync(Guid userId, OtpPurpose purpose, string codeHash, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (Rows.TryGetValue((userId, purpose), out var row) && row.CodeHash == codeHash && row.Attempts < row.MaxAttempts)
            {
                row.Attempts++;
                return Task.FromResult(true);
            }

            return Task.FromResult(false);
        }
    }

    public Task<bool> TryConsumeAsync(Guid userId, OtpPurpose purpose, string codeHash, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (Rows.TryGetValue((userId, purpose), out var row) && row.CodeHash == codeHash)
            {
                row.CodeHash = string.Empty;
                return Task.FromResult(true);
            }

            return Task.FromResult(false);
        }
    }

    public Task InvalidateAsync(
        Guid userId, OtpPurpose purpose, string? onlyIfCodeHash, DateTime? lastSentAt, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (Rows.TryGetValue((userId, purpose), out var row) && row.CodeHash != string.Empty &&
                (onlyIfCodeHash is null || row.CodeHash == onlyIfCodeHash))
            {
                row.CodeHash = string.Empty;
                if (lastSentAt is { } rewound)
                {
                    row.LastSentAt = rewound;
                }
            }

            return Task.CompletedTask;
        }
    }

    private static OtpCode Clone(OtpCode c) => new()
    {
        UserId = c.UserId, Purpose = c.Purpose, CodeHash = c.CodeHash, ExpiresAt = c.ExpiresAt,
        Attempts = c.Attempts, MaxAttempts = c.MaxAttempts, LastSentAt = c.LastSentAt,
        WindowStartedAt = c.WindowStartedAt, SendCount = c.SendCount
    };
}

public static class Mocks
{
    public static Mock<UserManager<ApplicationUser>> UserManager(IPasswordHasher<ApplicationUser>? hasher = null)
    {
        var store = new Mock<IUserStore<ApplicationUser>>();
        var manager = new Mock<UserManager<ApplicationUser>>(
            store.Object,
            Microsoft.Extensions.Options.Options.Create(new Microsoft.AspNetCore.Identity.IdentityOptions()),
            hasher ?? new PasswordHasher<ApplicationUser>(),
            Array.Empty<IUserValidator<ApplicationUser>>(),
            Array.Empty<IPasswordValidator<ApplicationUser>>(),
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            null!,
            NullLogger<UserManager<ApplicationUser>>.Instance);

        manager.Setup(m => m.UpdateAsync(It.IsAny<ApplicationUser>())).ReturnsAsync(IdentityResult.Success);
        return manager;
    }

    /// <summary>Registers users so FindByIdAsync/GetRolesAsync answer like a store would.</summary>
    public static void WithUsers(this Mock<UserManager<ApplicationUser>> manager,
        params (ApplicationUser User, string[] Roles)[] users)
    {
        foreach (var (user, roles) in users)
        {
            manager.Setup(m => m.FindByIdAsync(user.Id.ToString())).ReturnsAsync(user);
            manager.Setup(m => m.GetRolesAsync(user)).ReturnsAsync(roles.ToList());
        }
    }

    public static ApplicationUser User(string? phone = null, bool active = true) => new()
    {
        Id = Guid.NewGuid(),
        UserName = $"u{Guid.NewGuid():N}",
        Email = $"{Guid.NewGuid():N}@example.com",
        PhoneNumber = phone,
        IsActive = active,
        SecurityStamp = Guid.NewGuid().ToString()
    };
}

/// <summary>Captures audit entries in memory (what IdentityAuditWriter would add to the unit of work).</summary>
public sealed class RecordingAuditWriter : Identity.Domains.Audit.Services.IIdentityAuditWriter
{
    public sealed record Entry(
        string Action, Guid? TargetId, string TargetType, string? Reason, string? BeforeJson, string? AfterJson,
        Guid? ActorId);

    public List<Entry> Entries { get; } = new();

    public void Record(string action, Guid? targetId, string targetType = "User", string? reason = null,
        object? before = null, object? after = null, Guid? actorId = null) =>
        Entries.Add(new Entry(action, targetId, targetType, reason,
            Identity.Domains.Audit.Services.IdentityAuditWriter.Serialize(before),
            Identity.Domains.Audit.Services.IdentityAuditWriter.Serialize(after), actorId));
}
