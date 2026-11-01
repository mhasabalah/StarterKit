namespace Subscriptions.Domains.Subscriptions.Services;

public interface IFeatureService
{
    Task<bool> IsEnabledAsync(string key, CancellationToken cancellationToken = default);
    Task<int?> GetIntAsync(string key, CancellationToken cancellationToken = default);
    Task<string?> GetStringAsync(string key, CancellationToken cancellationToken = default);
}
