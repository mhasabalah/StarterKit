namespace Subscriptions.Contracts.Subscriptions.Queries;

public record GetAllFeaturesQuery(Guid SubscriberId) : IQuery<Dictionary<string, string>>;
