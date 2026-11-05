using MassTransit;

namespace Shared.Core.MultiTenancy.Messaging;

/// <summary>
/// MassTransit tenant propagation. Publish/send filters stamp the ambient tenant into the <see cref="HeaderName"/>
/// header; the consume filter restores it as the consumer's <see cref="TenantScope"/> (consumers run on another
/// async flow, so the ambient value would otherwise be lost). Registered by <c>AddMassTransitWithAssemblies</c>.
/// </summary>
public static class TenantMessageHeaders
{
    public const string HeaderName = "X-Tenant-Id";
    public const string HostScopeHeaderName = "X-Tenant-Host-Scope";
}

public sealed class TenantPublishFilter<T>(ITenantContext tenantContext) : IFilter<PublishContext<T>> where T : class
{
    public Task Send(PublishContext<T> context, IPipe<PublishContext<T>> next)
    {
        TenantMessageStamp.Stamp(context, tenantContext);
        return next.Send(context);
    }

    public void Probe(ProbeContext context) => context.CreateFilterScope("tenantPublish");
}

public sealed class TenantSendFilter<T>(ITenantContext tenantContext) : IFilter<SendContext<T>> where T : class
{
    public Task Send(SendContext<T> context, IPipe<SendContext<T>> next)
    {
        TenantMessageStamp.Stamp(context, tenantContext);
        return next.Send(context);
    }

    public void Probe(ProbeContext context) => context.CreateFilterScope("tenantSend");
}

public sealed class TenantConsumeFilter<T>(ITenantStore tenantStore, ILogger<TenantConsumeFilter<T>> logger)
    : IFilter<ConsumeContext<T>> where T : class
{
    public async Task Send(ConsumeContext<T> context, IPipe<ConsumeContext<T>> next)
    {
        if (context.Headers.TryGetHeader(TenantMessageHeaders.HeaderName, out var raw) &&
            Guid.TryParse(raw?.ToString(), out var tenantId))
        {
            var tenant = await tenantStore.FindByIdAsync(tenantId, context.CancellationToken);
            if (tenant is null)
            {
                // The tenant was purged after publishing: drop instead of processing tenant-less.
                logger.LogWarning("Skipping {Message}: tenant {TenantId} no longer exists", typeof(T).Name, tenantId);
                return;
            }

            if (tenant.Status == TenantStatus.Migrating)
            {
                // Writes would land in the database being left: retried later (bus retry policy).
                throw new TenantMigratingException();
            }

            using (context.Headers.TryGetHeader(TenantMessageHeaders.HostScopeHeaderName, out _)
                       ? TenantScope.BeginHostScope(tenant)
                       : TenantScope.Begin(tenant))
            {
                await next.Send(context);
            }

            return;
        }

        if (context.Headers.TryGetHeader(TenantMessageHeaders.HostScopeHeaderName, out _))
        {
            using (TenantScope.BeginHostScope())
            {
                await next.Send(context);
            }

            return;
        }

        await next.Send(context);
    }

    public void Probe(ProbeContext context) => context.CreateFilterScope("tenantConsume");
}

internal static class TenantMessageStamp
{
    public static void Stamp(SendContext context, ITenantContext tenantContext)
    {
        if (context.Headers.TryGetHeader(TenantMessageHeaders.HeaderName, out _))
            return; // explicit wins

        if (tenantContext.TenantId is { } tenantId)
        {
            context.Headers.Set(TenantMessageHeaders.HeaderName, tenantId.ToString());
        }

        if (tenantContext.IsHostScope)
        {
            context.Headers.Set(TenantMessageHeaders.HostScopeHeaderName, "true");
        }
    }
}
