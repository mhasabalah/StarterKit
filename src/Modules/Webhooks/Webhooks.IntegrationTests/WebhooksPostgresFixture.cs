using System.Collections.Concurrent;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Notification.Contracts;
using Notification.Contracts.Email;
using Notification.Contracts.Enums;
using Npgsql;
using Shared.Contracts.Mediation;
using Shared.Core.Data;
using Shared.Core.MultiTenancy;
using Shared.Core.Web;
using Tenancy.Contracts.Tenants;
using Testcontainers.PostgreSql;
using Webhooks.Data;
using Webhooks.Security;

namespace Webhooks.IntegrationTests;

/// <summary>
/// Webhooks as it runs in a MULTI-TENANT production deployment:
/// <list type="bullet">
/// <item>the REAL migration (tables, indexes, forced row-level security) applied as the admin;</item>
/// <item>the REAL module (<c>AddMultiTenancy</c> + <c>AddWebhooksModule</c>) connecting as a NON-superuser role so RLS is
/// enforced under the handlers;</item>
/// <item>two tenants entered with <see cref="TenantScope"/> exactly like tenant resolution does per request.</item>
/// </list>
/// Faked boundaries only: the outbound HTTP sender (switchable: scripted or the real guarded one), Hangfire (recording),
/// the tenant lookup/owner (Tenancy), notifications, and the clock.
/// </summary>
public sealed class WebhooksPostgresFixture : IAsyncLifetime
{
    private const string AppRole = "webhooks_app";
    private const string AppPassword = "webhooks_app_password";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine").Build();
    private ServiceProvider? _provider;

    public static readonly TenantInfo TenantA = new(Guid.Parse("aaaaaaaa-1111-0000-0000-000000000001"), "acme", "Acme");
    public static readonly TenantInfo TenantB = new(Guid.Parse("bbbbbbbb-2222-0000-0000-000000000002"), "globex", "Globex");
    public static readonly Guid OwnerOfA = Guid.Parse("0a0a0a0a-0000-0000-0000-00000000000a");
    public static readonly Guid OwnerOfB = Guid.Parse("0b0b0b0b-0000-0000-0000-00000000000b");

    public IServiceProvider Services => _provider ?? throw new InvalidOperationException("Not initialized");
    public string AdminConnectionString => _container.GetConnectionString();

    public MutableTimeProvider Clock { get; } = new();
    public RecordingJobClient Jobs { get; } = new();
    public SwitchableSender Sender { get; } = new();
    public FakeNotificationSender Notifications { get; } = new();

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();

        // 1. Real migration, as the admin (owner of the schema).
        var adminOptions = new DbContextOptionsBuilder<WebhooksDbContext>()
            .UseNpgsql(AdminConnectionString, b => b.UseModuleMigrationsHistory<WebhooksDbContext>())
            .UseSnakeCaseNamingConvention()
            .Options;
        await using (var admin = new WebhooksDbContext(adminOptions))
        {
            await admin.Database.MigrateAsync();
        }

        // 2. The application role: DML only, NOT a superuser and NOT the table owner -> RLS applies.
        await using (var connection = new NpgsqlConnection(AdminConnectionString))
        {
            await connection.OpenAsync();
            foreach (var sql in new[]
                     {
                         $"CREATE ROLE {AppRole} LOGIN PASSWORD '{AppPassword}' NOSUPERUSER NOBYPASSRLS",
                         $"GRANT USAGE ON SCHEMA webhooks TO {AppRole}",
                         $"GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA webhooks TO {AppRole}"
                     })
            {
                await using var command = new NpgsqlCommand(sql, connection);
                await command.ExecuteNonQueryAsync();
            }
        }

        var appConnection = new NpgsqlConnectionStringBuilder(AdminConnectionString)
        {
            Username = AppRole,
            Password = AppPassword
        }.ConnectionString;

        // 3. The real module, multi-tenant, as the app role.
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:starterkit"] = appConnection,
                ["MultiTenancy:Enabled"] = "true",
                // The receiver in these tests listens on loopback over plain http.
                ["Webhooks:RequireHttps"] = "false",
                ["Webhooks:AllowPrivateNetworks"] = "true",
                ["Webhooks:RequestTimeoutSeconds"] = "2",
                // Tests share one database; the per-tenant endpoint cap has its own test.
                ["Webhooks:MaxEndpointsPerTenant"] = "10000"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLocalization();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<TimeProvider>(Clock);
        services.AddMultiTenancy(configuration);
        services.AddSingleton<ITenantStore>(new FixedTenantStore(TenantA, TenantB));

        // Tenancy is not part of this module's tests: stub its owner lookup (the handler asks for it through ISender).
        services.AddTransient<IRequestHandler<GetTenantByIdQuery, TenantDto?>, FakeTenantHandler>();
        services.AddSingleton<INotificationSender>(Notifications);
        services.AddSingleton(TestHttp.HttpContext());

        services.AddWebhooksModule(configuration);

        services.RemoveAll<IBackgroundJobClient>();
        services.AddSingleton<IBackgroundJobClient>(Jobs);
        services.RemoveAll<IWebhookSender>();
        services.AddSingleton<IWebhookSender>(Sender);

        _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    public async ValueTask DisposeAsync()
    {
        if (_provider is not null)
            await _provider.DisposeAsync();
        await _container.DisposeAsync();
    }

    /// <summary>One HTTP request of a tenant member resolved to <paramref name="tenant"/>.</summary>
    public async Task<T> SendAsync<T>(TenantInfo tenant, IRequest<T> request)
    {
        using (TenantScope.Begin(tenant))
        {
            await using var scope = Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<ISender>().Send(request);
        }
    }

    /// <summary>Work inside <paramref name="tenant"/>'s scope with a fresh DI scope (EF filters + RLS apply).</summary>
    public async Task<T> InTenantAsync<T>(TenantInfo tenant, Func<IServiceProvider, Task<T>> work)
    {
        using (TenantScope.Begin(tenant))
        {
            await using var scope = Services.CreateAsyncScope();
            return await work(scope.ServiceProvider);
        }
    }

    public Task<T> InTenantDbAsync<T>(TenantInfo tenant, Func<WebhooksDbContext, Task<T>> work) =>
        InTenantAsync(tenant, sp => work(sp.GetRequiredService<WebhooksDbContext>()));

    /// <summary>Runs the delivery job exactly like Hangfire would (tenant restored from the enqueue-time scope).</summary>
    public Task<Webhooks.Domains.Deliveries.Jobs.WebhookAttemptOutcome> RunDeliveryAsync(TenantInfo tenant, Guid deliveryId) =>
        InTenantAsync(tenant, sp => sp.GetRequiredService<Webhooks.Domains.Deliveries.Jobs.DeliverWebhookJob>()
            .ExecuteAsync(deliveryId, CancellationToken.None));

    /// <summary>SQL as the DB owner (bypasses RLS): arranging corrupt/aged data and cross-tenant verification.</summary>
    public async Task<T?> AdminScalarAsync<T>(string sql)
    {
        await using var connection = new NpgsqlConnection(AdminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        var value = await command.ExecuteScalarAsync();
        return value is null or DBNull ? default : (T)Convert.ChangeType(value, typeof(T));
    }

    public async Task AdminExecAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(AdminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class WebhooksPostgresCollection : ICollectionFixture<WebhooksPostgresFixture>
{
    public const string Name = "webhooks-postgres";
}

// ----- fakes --------------------------------------------------------------------------------------------------------

public sealed class MutableTimeProvider : TimeProvider
{
    private DateTimeOffset _now = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;

    public void Set(DateTimeOffset now) => _now = now;
}

public sealed class FixedTenantStore(params TenantInfo[] tenants) : ITenantStore
{
    public Task<TenantInfo?> FindByIdAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
        Task.FromResult(tenants.FirstOrDefault(t => t.Id == tenantId));

    public Task<TenantInfo?> FindByIdentifierAsync(string identifier, CancellationToken cancellationToken = default) =>
        Task.FromResult(tenants.FirstOrDefault(t => t.Identifier == identifier));

    public Task<TenantInfo?> FindByHostAsync(string host, CancellationToken cancellationToken = default) =>
        Task.FromResult<TenantInfo?>(null);

    public Task<IReadOnlyList<TenantInfo>> GetServiceableTenantsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<TenantInfo>>(tenants);
}

public sealed class FakeTenantHandler : IRequestHandler<GetTenantByIdQuery, TenantDto?>
{
    public Task<TenantDto?> Handle(GetTenantByIdQuery request, CancellationToken cancellationToken)
    {
        var (tenant, owner) = request.TenantId == WebhooksPostgresFixture.TenantA.Id
            ? (WebhooksPostgresFixture.TenantA, WebhooksPostgresFixture.OwnerOfA)
            : (WebhooksPostgresFixture.TenantB, WebhooksPostgresFixture.OwnerOfB);
        return Task.FromResult<TenantDto?>(new TenantDto(tenant.Id, tenant.Identifier, tenant.Name, null, default, default,
            null, null, null, null, null, null, owner, DateTime.UtcNow, 1));
    }
}

public sealed class FakeNotificationSender : INotificationSender, INotificationBuilder
{
    private readonly ConcurrentQueue<(Guid User, EmailTemplate Template, Dictionary<string, object>? Model)> _sent = new();
    private Guid _user;
    private EmailTemplate _template;
    private Dictionary<string, object>? _model;

    public IReadOnlyList<(Guid User, EmailTemplate Template, Dictionary<string, object>? Model)> Sent => _sent.ToList();

    public INotificationBuilder For(Guid userId, NotificationCulture culture = NotificationCulture.En)
    {
        _user = userId;
        return this;
    }

    public INotificationBuilder ToEmail(string email, NotificationCulture culture = NotificationCulture.En, Guid? userId = null) => this;

    public INotificationBuilder WithEmail(EmailTemplate template, Dictionary<string, object>? model = null,
        EmailAttachment[]? attachments = null)
    {
        _template = template;
        _model = model;
        return this;
    }

    public INotificationBuilder WithWhatsApp(WhatsAppTemplate template, Dictionary<string, object>? model = null) => this;

    public INotificationBuilder WithInApp(Notification.Contracts.InApp.InAppContent content) => this;

    public INotificationBuilder WithPush(Notification.Contracts.Push.PushContent content) => this;

    public INotificationBuilder WithPush(bool sensitive = false) => this;

    public Task<Guid> SendAsync(NotificationChannel? forcedChannel = null, CancellationToken ct = default)
    {
        _sent.Enqueue((_user, _template, _model));
        return Task.FromResult(Guid.NewGuid());
    }
}

/// <summary>Hangfire stand-in that records enqueued / scheduled jobs (and their delay) instead of running them.</summary>
public sealed class RecordingJobClient : IBackgroundJobClient
{
    public sealed record Recorded(Job Job, IState State)
    {
        public Guid DeliveryId => (Guid)Job.Args[0];
        public bool IsDeliveryJob => Job.Type == typeof(Webhooks.Domains.Deliveries.Jobs.DeliverWebhookJob);

        /// <summary>Delay of a scheduled job; zero for an immediate enqueue.</summary>
        public TimeSpan Delay => State is ScheduledState scheduled ? scheduled.EnqueueAt - scheduled.ScheduledAt : TimeSpan.Zero;
    }

    private readonly ConcurrentQueue<Recorded> _jobs = new();

    public IReadOnlyList<Recorded> All => _jobs.ToList();

    public IReadOnlyList<Recorded> ForDelivery(Guid deliveryId) =>
        _jobs.Where(j => j.IsDeliveryJob && j.DeliveryId == deliveryId).ToList();

    public string Create(Job job, IState state)
    {
        _jobs.Enqueue(new Recorded(job, state));
        return Guid.NewGuid().ToString("N");
    }

    public bool ChangeState(string jobId, IState state, string expectedState) => true;
}

/// <summary>The sender the job talks to: scripted by default, or pointed at the REAL guarded sender.</summary>
public sealed class SwitchableSender : IWebhookSender
{
    public IWebhookSender Current { get; set; } = new ScriptedSender();

    public Task<WebhookSendResult> SendAsync(WebhookRequest request, CancellationToken cancellationToken) =>
        Current.SendAsync(request, cancellationToken);

    public ScriptedSender Scripted()
    {
        var scripted = new ScriptedSender();
        Current = scripted;
        return scripted;
    }
}

public sealed class ScriptedSender : IWebhookSender
{
    private readonly ConcurrentQueue<Func<WebhookRequest, Task<WebhookSendResult>>> _script = new();
    private readonly ConcurrentQueue<WebhookRequest> _requests = new();

    public IReadOnlyList<WebhookRequest> Requests => _requests.ToList();

    /// <summary>Used when the script runs out.</summary>
    public Func<WebhookRequest, Task<WebhookSendResult>> Fallback { get; set; } = _ => Task.FromResult(Ok());

    public ScriptedSender Then(WebhookSendResult result) => Then(_ => Task.FromResult(result));

    public ScriptedSender Then(Func<WebhookRequest, Task<WebhookSendResult>> step)
    {
        _script.Enqueue(step);
        return this;
    }

    public Task<WebhookSendResult> SendAsync(WebhookRequest request, CancellationToken cancellationToken)
    {
        _requests.Enqueue(request);
        return _script.TryDequeue(out var step) ? step(request) : Fallback(request);
    }

    public static WebhookSendResult Ok(int status = 200) => new(true, status, 12, null, "ok", WebhookFailureKind.None);

    public static WebhookSendResult Http(int status) => new(false, status, 12, $"HTTP {status}", "nope",
        status is 408 or 425 or 429 or >= 500 ? WebhookFailureKind.Retriable : WebhookFailureKind.Permanent);

    public static WebhookSendResult Timeout() =>
        new(false, null, 2000, "Request timed out.", null, WebhookFailureKind.Retriable);
}

internal static class TestHttp
{
    /// <summary>A permissive <see cref="IHttpContextService"/> (no HTTP request in these tests).</summary>
    public static IHttpContextService HttpContext() => new Moq.Mock<IHttpContextService> { DefaultValue = Moq.DefaultValue.Empty }.Object;
}
