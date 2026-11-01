using Shared.Core.DDD;

namespace Identity.Contracts.Onboarding.Events;

public record UserRegisteredEvent(Guid UserId) : IDomainEvent;
