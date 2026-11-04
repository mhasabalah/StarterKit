using System.Collections.Concurrent;
using System.Security.Claims;
using Attachment.Contracts.Attachments.Commands;
using Attachment.Contracts.Attachments.Dtos;
using Attachment.Contracts.Attachments.Queries;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Shared.Contracts.Mediation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shared.Contracts.Localization;
using Shared.Core.Authorization;
using Shared.Core.Localization.Services;
using Shared.Core.Web;
using Notification.Contracts;
using Notification.Contracts.Enums;
using Notification.Dispatch;
using Subscriptions.Contracts.Subscribers;
using Subscriptions.Data;
using Subscriptions.Domains.Plans.Models;
using Subscriptions.Domains.PaymentSettings.Models;
using Subscriptions.Domains.Subscriptions.Jobs;
using Testcontainers.PostgreSql;

namespace Subscriptions.IntegrationTests;

/// <summary>
/// One real PostgreSQL (Testcontainers, postgres:16-alpine) for the collection, wired with the REAL module
/// registration (<c>AddSubscriptionsModule</c>): real DbContext through Shared.Core AddCustomDbContext (Npgsql +
/// snake_case + legacy-timestamp switch + AuditableEntityInterceptor + DispatchDomainEventsInterceptor), the real
/// mediator pipeline (validation, concurrency, domain-event dispatch), real repositories, issuer, orchestrator, jobs.
/// Faked boundaries: HTTP user, subscriber directory (Identity), attachment upload (Attachment), Hangfire client
/// (jobs are captured and run explicitly) and the notification queue. Schema from <c>EnsureCreatedAsync</c>.
/// </summary>
public sealed class SubscriptionsPostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine").Build();
    private ServiceProvider _provider = default!;

    public FakeHttpContextService Http { get; } = new();
    public FakeBackgroundJobClient Jobs { get; } = new();
    public FakeNotificationJobEnqueuer Notifications { get; } = new();
    public FakeSubscriberInfoProvider Subscribers { get; } = new();
    public IServiceProvider Services => _provider;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:starterkit"] = _container.GetConnectionString(),
                ["Subscriptions:Tax:Rate"] = "0.15",
                ["Subscriptions:Tax:PricesIncludeTax"] = "true",
                ["Subscriptions:UpgradeSlaHours"] = "72",
                ["Subscriptions:TrustMinCycles"] = "2",
                ["Subscriptions:PaymentGateways:Fake:Enabled"] = "true",
                ["Subscriptions:PaymentGateways:Fake:Secret"] = FakeGatewaySecret,
                ["Subscriptions:Invoice:CompanyName"] = "Test Seller",
                ["Subscriptions:Invoice:TaxId"] = "VAT-123",
                ["Subscriptions:Invoice:NumberPrefix"] = "INV",
                ["Email:FromAddress"] = "admin@example.com",
                ["Subscriptions:AdminNotifications:Emails:0"] = BillingTeamEmail
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLocalization();
        services.AddDistributedMemoryCache();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IEnumLocalizationService, EnumLocalizationService>();
        services.AddSingleton<IHttpContextService>(Http);
        services.AddSingleton<IBackgroundJobClient>(Jobs);
        services.AddSingleton<INotificationJobEnqueuer>(Notifications);
        services.AddSingleton<INotificationSender, NotificationSender>();
        services.AddSingleton<ISubscriberInfoProvider>(Subscribers); // before the module's TryAdd default
        services.AddTransient<IRequestHandler<UploadAttachmentCommand, UploadAttachmentResult>, FakeUploadAttachmentHandler>();
        services.AddTransient<IRequestHandler<GetAttachmentByIdQuery, GetAttachmentByIdResult>, FakeGetAttachmentByIdHandler>();
        services.AddSubscriptionsModule(configuration);

        _provider = services.BuildServiceProvider();

        await using var scope = _provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SubscriptionsDbContext>();
        await db.Database.EnsureCreatedAsync();
        db.PaymentMethods.Add(PaymentMethod.Create("BankTransfer", "Bank", "ACC-1", null, null, 1));
        await db.SaveChangesAsync();
        PaymentMethodId = await db.PaymentMethods.Select(x => x.Id).FirstAsync();
    }

    public Guid PaymentMethodId { get; private set; }

    public const string FakeGatewaySecret = "fake-gateway-test-secret-0123456789";

    /// <summary>The configured admin notification recipient (Subscriptions:AdminNotifications:Emails).</summary>
    public const string BillingTeamEmail = "billing-team@example.com";

    public async Task DisposeAsync()
    {
        if (_provider is not null) await _provider.DisposeAsync();
        await _container.DisposeAsync();
    }

    /// <summary>Sends a request through the real pipeline in a fresh scope (= a new HTTP request) as the given user.</summary>
    public async Task<TResponse> SendAs<TResponse>(Guid? userId, IRequest<TResponse> request, bool admin = false)
    {
        Http.SetUser(userId, admin);
        await using var scope = _provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(request);
    }

    public Task<TResponse> SendAsAdmin<TResponse>(IRequest<TResponse> request) =>
        SendAs(TestIds.Admin, request, admin: true);

    public async Task<T> WithDb<T>(Func<SubscriptionsDbContext, Task<T>> work)
    {
        await using var scope = _provider.CreateAsyncScope();
        return await work(scope.ServiceProvider.GetRequiredService<SubscriptionsDbContext>());
    }

    public async Task WithScope(Func<IServiceProvider, Task> work)
    {
        await using var scope = _provider.CreateAsyncScope();
        await work(scope.ServiceProvider);
    }

    /// <summary>Creates an active, public plan directly in the database.</summary>
    public Task<Plan> CreatePlanAsync(string name, decimal price, string currency = "SAR", int cycle = 1, params string[] features) =>
        WithDb(async db =>
        {
            var plan = Plan.Create(LocalizedText.Create(name, name), null, price, currency, cycle, 10, 5, 3, isPublic: true);
            foreach (var feature in features)
                plan.AddFeature(feature);
            db.Plans.Add(plan);
            await db.SaveChangesAsync();
            return plan;
        });

    /// <summary>Runs every captured invoice delivery job (PDF + email), like a Hangfire worker would.</summary>
    public async Task RunInvoiceDeliveriesAsync()
    {
        foreach (var (method, args) in Jobs.Drain())
        {
            if (method.DeclaringType == typeof(InvoiceDeliveryJob))
            {
                var job = _provider.GetRequiredService<InvoiceDeliveryJob>();
                await job.DeliverAsync((Guid)args[0]!, (bool)args[1]!);
            }
        }
    }
}

[CollectionDefinition(Name)]
public sealed class SubscriptionsPostgresCollection : ICollectionFixture<SubscriptionsPostgresFixture>
{
    public const string Name = "subscriptions-postgres";
}

public static class TestIds
{
    public static readonly Guid Admin = Guid.Parse("bbbbbbbb-0000-0000-0000-00000000000a");
}

/// <summary>Mutable "current request user" shared by the interceptor, handlers and services.</summary>
public sealed class FakeHttpContextService : IHttpContextService
{
    private readonly AsyncLocal<(Guid? Id, string[] Roles)> _user = new();

    public void SetUser(Guid? userId, bool admin) =>
        _user.Value = (userId, userId is null ? [] : admin ? [RoleConstants.Admin] : [RoleConstants.User]);

    public string? GetUserId() => _user.Value.Id?.ToString();
    public long? GetUserIdAsLong() => null;
    public Guid? GetUserIdAsGuid() => _user.Value.Id;
    public string? GetUserName() => _user.Value.Id?.ToString();
    public string? GetEmail() => null;
    public IEnumerable<string> GetRoles() => _user.Value.Roles ?? [];
    public bool IsAuthenticated() => _user.Value.Id is not null;
    public string? GetClaimValue(string claimType) => null;
    public IEnumerable<string> GetClaimValues(string claimType) => [];
    public IEnumerable<Claim> GetAllClaims() => [];
    public bool HasClaim(string claimType, string? claimValue = null) => false;
    public string? GetIpAddress() => null;
    public string? GetUserAgent() => null;
    public string? GetRequestPath() => null;
    public string? GetRequestMethod() => null;
    public string? GetCorrelationId() => null;
    public string? GetHeaderValue(string headerKey) => null;
    public string? GetHost() => null;
    public string? GetScheme() => null;
    public string? GetRequestUrl() => null;
    public string? GetTenantId() => null;
}

/// <summary>Captures enqueued Hangfire jobs instead of running a server.</summary>
public sealed class FakeBackgroundJobClient : IBackgroundJobClient
{
    private readonly ConcurrentQueue<Job> _jobs = new();

    public string Create(Job job, IState state)
    {
        _jobs.Enqueue(job);
        return Guid.NewGuid().ToString("N");
    }

    public bool ChangeState(string jobId, IState state, string expectedState) => true;

    public IReadOnlyList<(System.Reflection.MethodInfo Method, object?[] Args)> Drain()
    {
        var drained = new List<(System.Reflection.MethodInfo, object?[])>();
        while (_jobs.TryDequeue(out var job))
            drained.Add((job.Method, job.Args.ToArray()));
        return drained;
    }

    public int PendingCount => _jobs.Count;
}

public sealed class FakeNotificationJobEnqueuer : INotificationJobEnqueuer
{
    public ConcurrentBag<NotificationMessage> Messages { get; } = [];

    public Task<Guid> EnqueueAsync(NotificationMessage message, NotificationChannel? forcedChannel = null, CancellationToken ct = default)
    {
        Messages.Add(message);
        return Task.FromResult(Guid.NewGuid());
    }

    public Task<string> ReEnqueueAsync(Guid notificationLogId, NotificationChannel? forcedChannel = null, CancellationToken ct = default) =>
        Task.FromResult(Guid.NewGuid().ToString());
}

public sealed class FakeSubscriberInfoProvider : ISubscriberInfoProvider
{
    public ConcurrentDictionary<Guid, SubscriberInfo> People { get; } = new();

    public Task<SubscriberInfo?> GetAsync(Guid subscriberId, CancellationToken cancellationToken = default) =>
        Task.FromResult<SubscriberInfo?>(People.TryGetValue(subscriberId, out var info)
            ? info
            : new SubscriberInfo(subscriberId, $"Subscriber {subscriberId:N}"[..20], $"{subscriberId:N}@example.com"));
}

/// <summary>
/// Stands in for the Attachment module: stores nothing, returns a new attachment id and remembers the SHA-256 of the
/// content (like the real module's ContentHash).
/// </summary>
public sealed class FakeUploadAttachmentHandler : IRequestHandler<UploadAttachmentCommand, UploadAttachmentResult>
{
    public static readonly ConcurrentDictionary<Guid, (string FileName, long Size, string ContentHash)> Uploaded = new();

    public async Task<UploadAttachmentResult> Handle(UploadAttachmentCommand request, CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        var hash = Convert.ToHexStringLower(await System.Security.Cryptography.SHA256.HashDataAsync(request.FileStream, cancellationToken));
        Uploaded[id] = (request.FileName, request.FileSize, hash);
        return new UploadAttachmentResult(
            id, request.FileName, $"/api/v1/attachments/{id}/download", null, request.FileSize, request.ContentType);
    }
}

/// <summary>Attachment metadata for uploads made through <see cref="FakeUploadAttachmentHandler"/>.</summary>
public sealed class FakeGetAttachmentByIdHandler : IRequestHandler<GetAttachmentByIdQuery, GetAttachmentByIdResult>
{
    public Task<GetAttachmentByIdResult> Handle(GetAttachmentByIdQuery request, CancellationToken cancellationToken)
    {
        if (!FakeUploadAttachmentHandler.Uploaded.TryGetValue(request.Id, out var file))
            throw new InvalidOperationException($"Unknown attachment {request.Id}.");

        return Task.FromResult(new GetAttachmentByIdResult(new AttachmentDto
        {
            Id = request.Id,
            FileName = file.FileName,
            OriginalFileName = file.FileName,
            ContentType = "image/png",
            FileSize = file.Size,
            FileUrl = $"/api/v1/attachments/{request.Id}/download",
            ContentHash = file.ContentHash
        }));
    }
}
