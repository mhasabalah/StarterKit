using Subscriptions.Contracts.Subscriptions.DTOs;

namespace Subscriptions.Contracts.Subscriptions.Queries;

public record GetRenewalStatusQuery(Guid SubscriberId) : IQuery<RenewalStatusDto?>;
