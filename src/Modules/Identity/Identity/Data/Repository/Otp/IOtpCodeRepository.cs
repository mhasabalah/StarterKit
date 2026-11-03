using Identity.Models;
using Identity.Services.Otp;

namespace Identity.Data.Repository.Otp;

public interface IOtpCodeRepository
{
    /// <summary>Current code row (untracked), or null.</summary>
    Task<OtpCode?> GetAsync(Guid userId, OtpPurpose purpose, CancellationToken ct = default);

    /// <summary>
    /// Atomically stores <paramref name="code"/> as the current code of (UserId, Purpose), replacing the previous
    /// code and resetting its attempts, ONLY if the last send is at or before <paramref name="cooldownBoundary"/> and
    /// the send cap allows it (window started at or before <paramref name="windowBoundary"/> = new window, or fewer
    /// than <paramref name="maxSendsPerWindow"/> sends in the current one). A single conditional UPDATE (or the
    /// first INSERT), so parallel issues can never both pass the gate. False when the gate refused.
    /// </summary>
    Task<bool> TryIssueAsync(
        OtpCode code, DateTime cooldownBoundary, DateTime windowBoundary, int maxSendsPerWindow,
        CancellationToken ct = default);

    /// <summary>
    /// Atomically uses one attempt: <c>attempts = attempts + 1 WHERE attempts &lt; max_attempts AND code_hash = @hash</c>.
    /// False when the code is locked, gone or was replaced, so parallel guesses can never exceed the limit.
    /// </summary>
    Task<bool> TryConsumeAttemptAsync(Guid userId, OtpPurpose purpose, string codeHash, CancellationToken ct = default);

    /// <summary>
    /// Single use: clears the code only if it still holds <paramref name="codeHash"/>. False if another request
    /// consumed or replaced it first. The row (cooldown and send-cap counters) is kept.
    /// </summary>
    Task<bool> TryConsumeAsync(Guid userId, OtpPurpose purpose, string codeHash, CancellationToken ct = default);

    /// <summary>
    /// Clears the current code (only if it holds <paramref name="onlyIfCodeHash"/>, when given) and, when
    /// <paramref name="lastSentAt"/> is given, rewinds the resend cooldown to it.
    /// </summary>
    Task InvalidateAsync(
        Guid userId, OtpPurpose purpose, string? onlyIfCodeHash, DateTime? lastSentAt, CancellationToken ct = default);
}
