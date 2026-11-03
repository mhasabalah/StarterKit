using Shared.Contracts.Events;

namespace Identity.Contracts.Onboarding.Events;

public record PhoneVerifiedEvent(Guid UserId) : IDomainEvent;
