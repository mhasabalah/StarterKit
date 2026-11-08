using System.Security.Claims;
using Hangfire;
using Shared.Contracts.Mediation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Shared.Core.Authorization;
using Shared.Core.Data.Seed;
using Shared.Core.MultiTenancy;
using Shared.Core.Web;
using Tenancy.Data;
using Testcontainers.PostgreSql;

namespace Tenancy.IntegrationTests;

/// <summary>
/// One real PostgreSQL (Testcontainers, postgres:16-alpine) per collection, wired with the REAL registrations:
/// <c>AddMultiTenancy</c> (enabled) + <c>AddTenancyModule</c> - real TenancyDbContext and interceptors, real mediator
/// pipeline (validation, concurrency, domain-event dispatch), real handlers, the tenant store and membership resolver.
/// The schema comes from the module's MIGRATIONS (unique indexes with the names the handlers translate on).
/// <para>
/// Faked boundaries only: the HTTP user (<see cref="FakeHttpContextService"/>) and Hangfire
/// (<see cref="Jobs"/>, provisioning is enqueued after tenant creation). Caches are in-memory.
/// Tests share the database; each test uses fresh identifiers / users so they stay independent.
/// </para>
/// </summary>
public sealed class TenancyPostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine").Build();
    private ServiceProvider _provider = default!;

    public FakeHttpContextService Http { get; } = new();
    public Mock<IBackgroundJobClient> Jobs { get; } = new();

    /// <summary>The Subscriptions side of <c>GetActivePlanLimitsQuery</c> (seat limits). Null = no plan limit.</summary>
    public FakePlanLimits PlanLimits { get; } = new();

    /// <summary>The Identity side of <c>GetUserSummariesQuery</c> (member names / e-mails).</summary>
    public FakeUserDirectory Users { get; } = new();

    /// <summary>The notification sender the invitation e-mail would go through (verify "nothing was queued").</summary>
    // DefaultValue.Mock: senders that chain the builder (invitation mailer, membership notifications) get mock builders.
    public Mock<Notification.Contracts.INotificationSender> Notifications { get; } = new() { DefaultValue = DefaultValue.Mock };

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:starterkit"] = _container.GetConnectionString(),
                ["MultiTenancy:Enabled"] = "true",
                ["AppOptions:Name"] = "StarterKit Tests"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddLocalization();
        services.AddMemoryCache();
        services.AddDistributedMemoryCache();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IHttpContextService>(Http);
        services.AddSingleton(Jobs.Object);
        // The invitation e-mail is queued through these; fail-soft in the mailer, so loose mocks are enough.
        services.AddSingleton(Notifications.Object);
        services.AddSingleton(Mock.Of<Notification.Contracts.INotificationLinkResolver>());
        services.AddSingleton<IRequestHandler<UserDirectory.Contracts.Queries.GetUserSummariesQuery,
            IReadOnlyList<UserDirectory.Contracts.Queries.UserSummaryDto>>>(Users);
        services.AddSingleton<IRequestHandler<UserDirectory.Contracts.Queries.GetUserIdsByEmailsQuery,
            IReadOnlyDictionary<string, Guid>>>(Users);
        services.AddSingleton<IRequestHandler<UserDirectory.Contracts.Queries.GetUserRolesQuery,
            IReadOnlyList<string>>>(Users);
        services.AddMultiTenancy(configuration);
        services.AddTenancyModule(configuration);
        services.AddSingleton<IRequestHandler<Subscriptions.Contracts.Subscriptions.Queries.GetActivePlanLimitsQuery,
            Subscriptions.Contracts.Subscriptions.DTOs.ActivePlanLimitsDto>>(PlanLimits);

        _provider = services.BuildServiceProvider();

        await using var scope = _provider.CreateAsyncScope();
        using (TenantScope.BeginHostScope())
        {
            await scope.ServiceProvider.GetRequiredService<TenancyDbContext>().Database.MigrateAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_provider is not null)
            await _provider.DisposeAsync();
        await _container.DisposeAsync();
    }

    /// <summary>Sends a request through the real pipeline in a fresh scope (= one HTTP request) as <paramref name="userId"/>.</summary>
    public async Task<TResponse> SendAs<TResponse>(Guid userId, IRequest<TResponse> request)
    {
        Http.SetUser(userId);
        await using var scope = _provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(request);
    }

    /// <summary>Same, inside the given tenant's ambient scope (what tenant resolution does for an HTTP request).</summary>
    public async Task<TResponse> SendInTenant<TResponse>(Guid tenantId, Guid userId, IRequest<TResponse> request)
    {
        using (TenantScope.Begin(new TenantInfo(tenantId, $"t-{tenantId:N}"[..12], "Tenant")))
        {
            return await SendAs(userId, request);
        }
    }

    /// <summary>Direct DB access in a fresh scope, as host (sees every tenant).</summary>
    public async Task<T> WithDb<T>(Func<TenancyDbContext, Task<T>> work)
    {
        await using var scope = _provider.CreateAsyncScope();
        using (TenantScope.BeginHostScope())
        {
            return await work(scope.ServiceProvider.GetRequiredService<TenancyDbContext>());
        }
    }

    public async Task WithServices(Func<IServiceProvider, Task> work)
    {
        await using var scope = _provider.CreateAsyncScope();
        await work(scope.ServiceProvider);
    }
}

[CollectionDefinition(Name)]
public sealed class TenancyPostgresCollection : ICollectionFixture<TenancyPostgresFixture>
{
    public const string Name = "tenancy-postgres";
}

/// <summary>Mutable "current request user". Not a platform admin (self-service paths).</summary>
public sealed class FakeHttpContextService : IHttpContextService
{
    private Guid? _userId;

    /// <summary>
    /// When set, user and roles come from this service instead (the REAL <see cref="HttpContextService"/> without an
    /// HTTP request answers with the <see cref="ActingUser"/> of a background job).
    /// </summary>
    public IHttpContextService? Real { get; set; }

    public void SetUser(Guid? userId) => _userId = userId;

    public string? GetUserId() => Real is null ? _userId?.ToString() : Real.GetUserId();
    public long? GetUserIdAsLong() => null;
    public Guid? GetUserIdAsGuid() => Real is null ? _userId : Real.GetUserIdAsGuid();
    public string? GetUserName() => GetUserId();
    public string? GetEmail() => null;
    public IEnumerable<string> GetRoles() => Real is not null ? Real.GetRoles() : _userId is null ? [] : [RoleConstants.User];
    public bool IsAuthenticated() => _userId is not null;
    public string? GetClaimValue(string claimType) => null;
    public IEnumerable<string> GetClaimValues(string claimType) => [];
    public IEnumerable<Claim> GetAllClaims() => [];
    public bool HasClaim(string claimType, string? claimValue = null) => false;
    public string? GetIpAddress() => "127.0.0.1";
    public string? GetUserAgent() => "tests";
    public string? GetRequestPath() => null;
    public string? GetRequestMethod() => null;
    public string? GetCorrelationId() => null;
    public string? GetHeaderValue(string headerKey) => null;
    public string? GetHost() => null;
    public string? GetScheme() => null;
    public string? GetRequestUrl() => null;
    public string? GetTenantId() => null;
}

/// <summary>Answers <c>GetActivePlanLimitsQuery</c> with a settable <see cref="MaxUsers"/>; no plan when null.</summary>
public sealed class FakePlanLimits : IRequestHandler<Subscriptions.Contracts.Subscriptions.Queries.GetActivePlanLimitsQuery,
    Subscriptions.Contracts.Subscriptions.DTOs.ActivePlanLimitsDto>
{
    public int? MaxUsers { get; set; }

    public Task<Subscriptions.Contracts.Subscriptions.DTOs.ActivePlanLimitsDto> Handle(
        Subscriptions.Contracts.Subscriptions.Queries.GetActivePlanLimitsQuery request, CancellationToken cancellationToken) =>
        MaxUsers is null
            ? throw new Shared.Core.Exceptions.NotFoundException("No active plan.")
            : Task.FromResult(new Subscriptions.Contracts.Subscriptions.DTOs.ActivePlanLimitsDto(null, null, MaxUsers, "Active", null));
}

/// <summary>Answers the user-directory queries from settable maps (users unknown to it are omitted).</summary>
public sealed class FakeUserDirectory :
    IRequestHandler<UserDirectory.Contracts.Queries.GetUserSummariesQuery, IReadOnlyList<UserDirectory.Contracts.Queries.UserSummaryDto>>,
    IRequestHandler<UserDirectory.Contracts.Queries.GetUserIdsByEmailsQuery, IReadOnlyDictionary<string, Guid>>,
    IRequestHandler<UserDirectory.Contracts.Queries.GetUserRolesQuery, IReadOnlyList<string>>
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, UserDirectory.Contracts.Queries.UserSummaryDto> _users = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, string[]> _roles = new();

    public void Add(Guid userId, string email, string displayName) =>
        _users[userId] = new UserDirectory.Contracts.Queries.UserSummaryDto(userId, displayName, email, null, true);

    public void SetRoles(Guid userId, params string[] roles) => _roles[userId] = roles;

    public Task<IReadOnlyList<UserDirectory.Contracts.Queries.UserSummaryDto>> Handle(
        UserDirectory.Contracts.Queries.GetUserSummariesQuery request, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<UserDirectory.Contracts.Queries.UserSummaryDto>>(
            request.UserIds.Where(_users.ContainsKey).Select(id => _users[id]).ToList());

    public Task<IReadOnlyDictionary<string, Guid>> Handle(
        UserDirectory.Contracts.Queries.GetUserIdsByEmailsQuery request, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<string, Guid>>(_users.Values
            .Where(u => u.Email is not null && request.Emails.Contains(u.Email, StringComparer.OrdinalIgnoreCase))
            .ToDictionary(u => u.Email!, u => u.UserId, StringComparer.OrdinalIgnoreCase));

    public Task<IReadOnlyList<string>> Handle(
        UserDirectory.Contracts.Queries.GetUserRolesQuery request, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<string>>(_roles.GetValueOrDefault(request.UserId) ?? []);
}
