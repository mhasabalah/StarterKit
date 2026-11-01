namespace Subscriptions.Domains.Subscriptions.Services;

public record UpgradeTrustEvaluation(
    bool IsDeferred,
    decimal ProratedAmount,
    DateTimeOffset? ProofRequiredBy);

public interface IUpgradeTrustService
{
    Task<UpgradeTrustEvaluation> EvaluateAsync(
        Subscription subscription,
        Plan newPlan,
        CancellationToken ct = default);
}
