using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Notification.Contracts;
using Notification.Contracts.Email;
using Notification.Contracts.Enums;
using Notification.Contracts.InApp;
using Shared.Contracts.Mediation;
using Shared.Core.Data;
using Shared.Core.MultiTenancy;
using Subscriptions.Contracts.Subscribers;
using Subscriptions.Data;
using Subscriptions.Data.Repositories;
using Subscriptions.Options;

namespace Subscriptions.Tests.TestSupport;

/// <summary>What one billing notification send looked like, captured at <c>SendAsync</c> time.</summary>
internal sealed class RecordedSend
{
    public Guid? UserId { get; init; }
    public List<EmailTemplate> Emails { get; } = [];
    public List<InAppContent> InApps { get; } = [];

    /// <summary>One entry per <c>WithPush(sensitive)</c> call.</summary>
    public List<bool> Pushes { get; } = [];

    public NotificationChannel? ForcedChannel { get; set; }
    public int SendCount { get; set; }

    /// <summary>The ambient tenant while <c>SendAsync</c> ran (null = none).</summary>
    public TenantInfo? TenantDuringSend { get; set; }
}

/// <summary>A recording <see cref="INotificationSender"/>: every <c>For/ToEmail</c> chain becomes one <see cref="RecordedSend"/>.</summary>
internal sealed class RecordingNotificationSender
{
    public Mock<INotificationSender> Mock { get; } = new();
    public List<RecordedSend> Sends { get; } = [];

    public RecordingNotificationSender()
    {
        Mock.Setup(s => s.For(It.IsAny<Guid>(), It.IsAny<NotificationCulture>()))
            .Returns((Guid userId, NotificationCulture _) => NewBuilder(userId));
        Mock.Setup(s => s.ToEmail(It.IsAny<string>(), It.IsAny<NotificationCulture>(), It.IsAny<Guid?>()))
            .Returns((string _, NotificationCulture _, Guid? userId) => NewBuilder(userId));
    }

    public INotificationSender Object => Mock.Object;

    private INotificationBuilder NewBuilder(Guid? userId)
    {
        var send = new RecordedSend { UserId = userId };
        Sends.Add(send);
        var builder = new Mock<INotificationBuilder>();
        builder.Setup(b => b.WithEmail(It.IsAny<EmailTemplate>(), It.IsAny<Dictionary<string, object>?>(), It.IsAny<EmailAttachment[]?>()))
            .Returns(builder.Object)
            .Callback<EmailTemplate, Dictionary<string, object>?, EmailAttachment[]?>((t, _, _) => send.Emails.Add(t));
        builder.Setup(b => b.WithInApp(It.IsAny<InAppContent>()))
            .Returns(builder.Object).Callback<InAppContent>(c => send.InApps.Add(c));
        builder.Setup(b => b.WithPush(It.IsAny<bool>()))
            .Returns(builder.Object).Callback<bool>(sensitive => send.Pushes.Add(sensitive));
        builder.Setup(b => b.SendAsync(It.IsAny<NotificationChannel?>(), It.IsAny<CancellationToken>()))
            .Returns((NotificationChannel? channel, CancellationToken _) =>
            {
                send.SendCount++;
                send.ForcedChannel = channel;
                send.TenantDuringSend = TenantScope.Current?.Tenant;
                return Task.FromResult(Guid.NewGuid());
            });
        return builder.Object;
    }
}

internal sealed class FixedTenantScopeFactory(TenantInfo? tenant, bool multiTenant = true) : ITenantScopeFactory
{
    public bool IsMultiTenant => multiTenant;

    public Task<TenantInfo?> FindAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
        Task.FromResult(tenant);
}

/// <summary>Runs the unit of work against a fixed provider (no scope per call).</summary>
internal sealed class InlineUnitOfWorkRunner(IServiceProvider services) : IUnitOfWorkRunner
{
    public Task RunAsync(Func<IServiceProvider, CancellationToken, Task> work, CancellationToken cancellationToken = default) =>
        work(services, cancellationToken);

    public Task<TResult> RunAsync<TResult>(
        Func<IServiceProvider, CancellationToken, Task<TResult>> work, CancellationToken cancellationToken = default) =>
        work(services, cancellationToken);
}

/// <summary>
/// Everything a billing notification sender needs, wired with fakes: the recording sender, a multi-tenant
/// <see cref="ITenantScopeFactory"/> that knows <see cref="Tenant"/>, an empty in-memory <see cref="SubscriptionsDbContext"/>
/// and loose mocks for the rest. Build a sender with <see cref="Create{T}"/> (constructor injection from this provider).
/// </summary>
internal sealed class BillingNotificationHarness
{
    public Guid SubscriberId { get; } = Guid.NewGuid();
    public Guid SubscriptionId { get; } = Guid.NewGuid();

    /// <summary>The subscriber IS the tenant, so the tenant's id equals the subscriber id.</summary>
    public TenantInfo Tenant { get; }

    public RecordingNotificationSender Notifications { get; } = new();
    public Mock<ISubscriptionRepository> Subscriptions { get; } = new();
    public Mock<ISender> Sender { get; } = new();
    public SubscriptionsDbContext DbContext { get; }
    public IServiceProvider Services { get; }

    public BillingNotificationHarness()
    {
        Tenant = new TenantInfo(SubscriberId, "acme", "Acme");
        DbContext = new SubscriptionsDbContext(new DbContextOptionsBuilder<SubscriptionsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Notifications.Object);
        services.AddSingleton<ITenantScopeFactory>(new FixedTenantScopeFactory(Tenant));
        services.AddSingleton(new Mock<ISubscriberInfoProvider>().Object);
        services.AddSingleton(Sender.Object);
        services.AddSingleton(new Mock<IPublisher>().Object);
        services.AddSingleton(new Mock<IDomainEventDispatcher>().Object);
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(new SubscriptionOptions()));
        services.AddSingleton(Subscriptions.Object);
        services.AddSingleton(DbContext);
        services.AddSingleton<IUnitOfWorkRunner>(sp => new InlineUnitOfWorkRunner(sp));
        Services = services.BuildServiceProvider();
    }

    public T Create<T>() where T : class => ActivatorUtilities.CreateInstance<T>(Services);
}
