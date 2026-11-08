using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Shared.Core.Messaging.Sagas;

/// <summary>
/// The ONLY way to register a saga (ADR-0023). Saga state is business state of the owning module: it lives in that
/// module's DbContext (its schema, its migrations, its tenant rules) and is loaded and saved by MassTransit's EF Core
/// repository, so it survives restarts and is shared by every replica. The bus never falls back to the in-memory saga
/// repository; <c>SagaArchitectureTests</c> fails for a saga registered any other way.
/// </summary>
public static class DurableSagaExtensions
{
    /// <summary>
    /// Registers <typeparamref name="TMachine"/> with its state in <typeparamref name="TDbContext"/>. Call it from the
    /// module manifest's <c>ConfigureMessaging</c>. Map the state with a <see cref="SagaStateConfiguration{TState}"/>
    /// and add a migration (plus RLS when the context is a tenant-data context).
    /// <list type="bullet">
    /// <item><b>Pessimistic concurrency</b> (<c>SELECT ... FOR UPDATE</c>): events of one instance are applied one
    /// after another, so concurrent events never lose an update.</item>
    /// <item><b>In-memory outbox</b> on the saga endpoint: messages the saga publishes or sends leave only after its
    /// state is committed. If the save fails, nothing is sent, and the redelivered inbound message replays the step
    /// (at-least-once, like every consumer).</item>
    /// </list>
    /// </summary>
    public static ISagaRegistrationConfigurator<TState> AddDurableSagaStateMachine<TMachine, TState, TDbContext>(
        this IBusRegistrationConfigurator bus)
        where TMachine : class, SagaStateMachine<TState>
        where TState : class, SagaStateMachineInstance
        where TDbContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(bus);

        return bus.AddSagaStateMachine<TMachine, TState>((context, saga) => saga.UseInMemoryOutbox(context))
            .EntityFrameworkRepository(repository =>
            {
                repository.ConcurrencyMode = ConcurrencyMode.Pessimistic;
                repository.ExistingDbContext<TDbContext>();
                repository.UsePostgres();
            });
    }
}
