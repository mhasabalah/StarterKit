namespace ModuleName.Domains.Items.Events;

/// <summary>
/// Same-module reaction (domain event): published after the unit of work commits. Handle it with an
/// <c>INotificationHandler&lt;ItemCreatedEvent&gt;</c> in this module. Other modules react to integration events instead.
/// </summary>
public record ItemCreatedEvent(Guid ItemId, string Code) : Shared.Contracts.Events.IDomainEvent;
