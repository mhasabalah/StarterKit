using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Shared.Core.Messaging.Sagas;

/// <summary>
/// Base EF mapping of a saga state (ADR-0023). The key is the <see cref="CorrelatedBy{TKey}.CorrelationId"/>, which
/// the message that starts the saga assigns, never the database. Apply it in the owning module's DbContext
/// (<c>builder.ApplyConfiguration(new OrderFulfilmentStateConfiguration())</c>, before <c>ToSnakeCaseTables()</c>).
/// <para>
/// In a tenant-data context, the state also implements <c>IMustHaveTenant</c>:
/// <list type="bullet">
/// <item>the bus restores the message's tenant (<c>TenantConsumeFilter</c>);</item>
/// <item><c>TenantEntityInterceptor</c> stamps <c>TenantId</c> on insert;</item>
/// <item>the named tenant filter scopes the lookup;</item>
/// <item>the table needs <c>EnableTenantRowLevelSecurity</c> in its migration.</item>
/// </list>
/// Never use <c>Entity&lt;T&gt;</c> / <c>Aggregate&lt;T&gt;</c> as the base class: the repository saves the state
/// itself, and the state carries no domain events.
/// </para>
/// </summary>
public abstract class SagaStateConfiguration<TState> : IEntityTypeConfiguration<TState>
    where TState : class, SagaStateMachineInstance
{
    /// <summary>Maximum length of a state-name column (state names are short identifiers).</summary>
    public const int StateNameMaxLength = 64;

    public void Configure(EntityTypeBuilder<TState> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.HasKey(state => state.CorrelationId);
        builder.Property(state => state.CorrelationId).ValueGeneratedNever();
        ConfigureState(builder);
    }

    /// <summary>
    /// Maps the rest of the state: the current-state column
    /// (<c>builder.Property(x => x.CurrentState).HasMaxLength(StateNameMaxLength).IsRequired()</c>), data columns and
    /// indexes.
    /// </summary>
    protected abstract void ConfigureState(EntityTypeBuilder<TState> builder);
}
