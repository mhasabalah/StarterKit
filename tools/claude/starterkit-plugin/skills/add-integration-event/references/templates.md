# add-integration-event templates

## Contract (`{{Publisher}}.Contracts/{{Aggregate}}s/IntegrationEvents/{{Aggregate}}ActivatedIntegrationEvent.cs`)

```csharp
using MassTransit;
using Shared.Contracts.Messaging;

namespace {{Publisher}}.Contracts.{{Aggregate}}s.IntegrationEvents;

[MessageUrn("{{publisher}}.{{aggregate}}-activated.v1")]   // stable wire name - never change it
public sealed class {{Aggregate}}ActivatedIntegrationEvent : IntegrationEvent
{
    public Guid {{Aggregate}}Id { get; init; }
    public DateTime ActivatedAt { get; init; }
}
```

`MessageUrn` comes from the `MassTransit` namespace. If the Contracts project cannot see it, check how
`Catalog.Contracts` gets it; Shared.Contracts exposes it transitively.

## Raise it in the aggregate

```csharp
public void Activate(DateTime now)
{
    Status = {{Aggregate}}Status.Active;
    AddDomainEvent(new {{Aggregate}}ActivatedEvent(Id));                       // same-module reactions
    AddIntegrationEvent(new {{Aggregate}}ActivatedIntegrationEvent           // other modules, via outbox
        { {{Aggregate}}Id = Id, ActivatedAt = now });
}
```

## Consumer: trigger, re-read, act idempotently

```csharp
using MassTransit;
using {{Publisher}}.Contracts.{{Aggregate}}s.IntegrationEvents;
using {{Publisher}}.Contracts.{{Aggregate}}s.Queries;

namespace {{Reactor}}.Domains.{{Area}}.Consumers;

/// <summary>
/// At-least-once, possibly out of order: the event is only a trigger. The decision is made from the publisher's
/// CURRENT state, so a duplicate is a no-op. Runs outside the mediator, so domain events are dispatched explicitly.
/// </summary>
public sealed class {{Aggregate}}ActivatedConsumer(
    {{Reactor}}DbContext dbContext,
    ISender sender,
    IDomainEventDispatcher dispatcher,
    ILogger<{{Aggregate}}ActivatedConsumer> logger)
    : IConsumer<{{Aggregate}}ActivatedIntegrationEvent>
{
    public async Task Consume(ConsumeContext<{{Aggregate}}ActivatedIntegrationEvent> context)
    {
        var ct = context.CancellationToken;
        var current = await sender.Send(new Get{{Aggregate}}ByIdQuery(context.Message.{{Aggregate}}Id), ct);
        if (current is null) return;                                   // gone: nothing to do

        var projection = await dbContext.{{Projection}}s.FirstOrDefaultAsync(p => p.SourceId == current.Id, ct);
        if (projection is not null && projection.IsActive) return;      // already applied (duplicate delivery)

        // ... create/update local state from `current` (NOT from the message payload) ...
        await dbContext.SaveChangesAsync(ct);
        await dispatcher.DispatchPendingAsync(ct);                     // publish what the save raised

        logger.LogInformation("Applied {{Aggregate}} {Id} activation", current.Id);
    }
}
```

A consumer that only calls a command can use `IUnitOfWorkRunner` instead:

```csharp
public sealed class XConsumer(IUnitOfWorkRunner unitOfWork) : IConsumer<XIntegrationEvent>
{
    public Task Consume(ConsumeContext<XIntegrationEvent> context) =>
        unitOfWork.RunAsync((services, ct) =>
            services.GetRequiredService<ISender>().Send(new DoThingCommand(context.Message.Id), ct),
            context.CancellationToken);
}
```

## Consumer unit test

```csharp
[Fact]
public async Task Consume_Duplicate_IsNoOp() { /* arrange the already-applied state; Consume twice; assert one change, no throw */ }

[Fact]
public async Task Consume_WhenSourceNoLongerExists_ReturnsWithoutError() { /* sender returns null → no save */ }
```

Use `Mock<ConsumeContext<T>>`, with `Message` and `CancellationToken` set up.
