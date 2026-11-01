using Shared.Core.DDD;

namespace Identity.Contracts.Onboarding.Events;

public record OnboardingTimedOutEvent(Guid UserId) : IDomainEvent;
