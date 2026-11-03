using Shared.Contracts.Events;

namespace Identity.Contracts.Onboarding.Events;

public record UserRegisteredEvent(Guid UserId) : IDomainEvent;
