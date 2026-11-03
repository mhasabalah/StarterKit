using Shared.Contracts.Events;

namespace Identity.Contracts.Onboarding.Events;

public record OnboardingTimedOutEvent(Guid UserId) : IDomainEvent;
