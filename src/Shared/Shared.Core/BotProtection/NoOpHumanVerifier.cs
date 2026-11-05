namespace Shared.Core.BotProtection;

/// <summary>Used when <c>BotProtection:Enabled=false</c> (dev/tests): every request is considered human.</summary>
public sealed class NoOpHumanVerifier : IHumanVerifier
{
    public Task<HumanVerificationResult> VerifyAsync(string? token, string? remoteIp, string action, CancellationToken cancellationToken) =>
        Task.FromResult(HumanVerificationResult.Passed);
}
