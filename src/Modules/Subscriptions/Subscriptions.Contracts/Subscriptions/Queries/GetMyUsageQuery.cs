using Subscriptions.Contracts.Subscriptions.DTOs;

namespace Subscriptions.Contracts.Subscriptions.Queries;

/// <summary>Usage of every metered feature of the subscriber's current plan.</summary>
public record GetMyUsageQuery(Guid SubscriberId) : IQuery<List<UsageDto>>;
