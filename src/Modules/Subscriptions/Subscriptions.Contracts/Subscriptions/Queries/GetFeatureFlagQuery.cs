namespace Subscriptions.Contracts.Subscriptions.Queries;

public record GetFeatureFlagQuery(Guid SubscriberId, string Key) : IQuery<string?>;
