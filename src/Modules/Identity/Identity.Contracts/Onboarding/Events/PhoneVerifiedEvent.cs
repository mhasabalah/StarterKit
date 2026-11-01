using Shared.Core.DDD;

namespace Identity.Contracts.Onboarding.Events;

public record PhoneVerifiedEvent(Guid UserId) : IDomainEvent;
