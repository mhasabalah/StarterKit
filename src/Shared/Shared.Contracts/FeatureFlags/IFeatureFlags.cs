namespace Shared.Contracts.FeatureFlags;

/// <summary>
/// Runtime feature flags: switch behaviour on or off without a deploy, per tenant, per user or by percentage rollout.
/// The flags are evaluated for the AMBIENT caller (current tenant and user), so callers just ask for a key.
/// <para>
/// This is our own port (wiki/Decisions/0008-feature-flags.md), like the mediator: modules depend on it, never on a
/// flag product. The default implementation reads <c>FeatureFlags:Defaults</c> from configuration; the Tenancy module
/// replaces it with the database-backed one (overrides, rollout, kill switch). Implementations never throw because a
/// cache or the store is unavailable: they fall back to the configured defaults.
/// </para>
/// Evaluation order: kill switch, user override, tenant override, percentage rollout, flag default, configured default.
/// An unknown flag is off unless <c>FeatureFlags:Defaults:&lt;key&gt;</c> says otherwise.
/// </summary>
public interface IFeatureFlags
{
    /// <summary>Is <paramref name="key"/> enabled for the current tenant and user?</summary>
    Task<bool> IsEnabledAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Every known flag (stored and configured) with its value for the current tenant and user.</summary>
    Task<IReadOnlyDictionary<string, bool>> GetAllAsync(CancellationToken cancellationToken = default);
}
