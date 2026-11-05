using Identity.Data;
using Identity.Data.Repository.Otp;
using Identity.Domains.Audit.Services;
using Identity.Domains.Auth.Services;
using Identity.Domains.Users.Services;
using Identity.Services.TwoFactor;
using Shared.Contracts.Mediation;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Identity.Data.Repository.RefreshTokens;
using Identity.Domains.Roles.Constants;
using Identity.Domains.Roles.Models;
using Identity.Domains.Users.Models;
using Identity.Options;
using Identity.Services.Otp;
using Identity.Services.Phone;
using Identity.Services.Sessions;
using Identity.Services.Sms;
using Identity.Services.Token;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Core.Extensions;
using Shared.Core.Phone;
using Shared.Core.Web;
using Tenancy.Contracts.Tenants;
using Testcontainers.PostgreSql;

namespace Identity.IntegrationTests;

/// <summary>
/// One PostgreSQL container for the collection; the real Identity DbContext (EnsureCreated from the model, no
/// migrations), real UserManager, repositories and session/OTP services. Redis is replaced by the in-memory
/// IDistributedCache (TokenBlacklistService only needs IDistributedCache semantics).
/// </summary>
public sealed class IdentityPostgresFixture : IAsyncLifetime
{
    public const string OtpKey = "integration-test-otp-key-0123456789abcdef";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine").Build();

    public ServiceProvider Services { get; private set; } = null!;

    /// <summary>The "current HTTP user" seen by handlers; tests set it per scope.</summary>
    public Guid? CurrentUserId { get; set; }

    /// <summary>Identity default: 5 strikes lock the account.</summary>
    public const int MaxFailedAccessAttempts = 5;

    private readonly FixedHttpContextAccessor _httpContextAccessor = new();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        // Mirror Shared.Core AddCustomDbContext (snake_case naming; no legacy timestamp switch -> timestamptz).
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLocalization();
        services.AddDataProtection();
        services.AddDistributedMemoryCache();
        services.AddSingleton(TimeProvider.System);

        services.AddDbContext<ApplicationIdentityDbContext>(o => o
            .UseNpgsql(_container.GetConnectionString())
            .UseSnakeCaseNamingConvention());

        services.AddIdentityCore<ApplicationUser>(o =>
            {
                o.Password.RequireNonAlphanumeric = false;
                o.User.RequireUniqueEmail = true;
                o.Lockout.AllowedForNewUsers = true;
                o.Lockout.MaxFailedAccessAttempts = MaxFailedAccessAttempts;
            })
            .AddRoles<ApplicationRole>()
            .AddEntityFrameworkStores<ApplicationIdentityDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders();
        services.Replace(ServiceDescriptor.Scoped<IUserStore<ApplicationUser>, ProtectedUserStore>());

        // SignInManager checks the "remember this 2FA client" cookie of 2FA users: give it an HTTP context.
        services.AddAuthentication().AddCookie(IdentityConstants.TwoFactorRememberMeScheme);
        services.AddSingleton<IHttpContextAccessor>(_httpContextAccessor);

        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(new JwtOptions
        {
            SecretKey = "integration-test-jwt-secret-key-0123456789",
            Issuer = "test",
            Audience = "test",
            AccessTokenExpirationMinutes = 15,
            RefreshTokenExpirationDays = 7
        }));
        services.Configure<IdentitySecurityOptions>(o => o.OtpHmacKey = OtpKey);
        services.Configure<PhoneVerificationOptions>(o => { });
        services.Configure<PasswordResetOptions>(o => { });
        services.Configure<LoginPolicyOptions>(o => { });
        services.Configure<TwoFactorOptions>(o => o.EnforcementMode = TwoFactorEnforcementMode.Enforce);

        var http = new Mock<IHttpContextService>();
        http.Setup(h => h.GetUserIdAsGuid()).Returns(() => CurrentUserId);
        http.Setup(h => h.GetIpAddress()).Returns("127.0.0.1");
        http.Setup(h => h.GetUserAgent()).Returns("integration-tests");
        services.AddSingleton(http.Object);

        services.AddSingleton<IPhoneValidationService, PhoneValidationService>();
        services.AddScoped<IPhoneNumberNormalizer, PhoneNumberNormalizer>();
        services.AddScoped<IJwtTokenGenerator, JwtTokenGenerator>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IAuthSessionService, AuthSessionService>();
        services.AddScoped<ISessionRevoker, SessionRevoker>();
        services.AddScoped<ISessionManager, SessionManager>();
        services.AddScoped<Identity.Data.Repository.ApiKeys.IApiKeyRepository, Identity.Data.Repository.ApiKeys.ApiKeyRepository>();
        services.AddSingleton<ITokenBlacklistService, TokenBlacklistService>();
        services.AddScoped<IOtpCodeRepository, OtpCodeRepository>();
        services.AddScoped<IOtpCodeService, OtpCodeService>();
        services.AddScoped<IRoleHierarchyGuard, RoleHierarchyGuard>();
        services.AddScoped<ITwoFactorService, TwoFactorService>();
        services.AddSingleton<ITotpReplayGuard>(sp =>
            new DistributedCacheTotpReplayGuard(sp.GetRequiredService<Microsoft.Extensions.Caching.Distributed.IDistributedCache>()));
        services.AddScoped<ITwoFactorChallengeService, TwoFactorChallengeService>();
        services.AddScoped<ILoginCompletionService, LoginCompletionService>();
        services.AddScoped<IIdentityAuditWriter, IdentityAuditWriter>();
        services.AddScoped<IIdentityUnitOfWork, IdentityUnitOfWork>();

        // Real mediator pipeline incl. the audited-command transaction behavior (as registered by IdentityModule).
        services.AddMediatorHandlers(IdentityRoot.AssemblyName);
        services.TryAddEnumerable(ServiceDescriptor.Transient(
            typeof(IPipelineBehavior<,>), typeof(AuditedCommandTransactionBehavior<,>)));

        // Tenancy answers "which tenants does this user own" in production; here the test decides.
        services.AddSingleton(OwnedTenants);
        services.AddSingleton<IRequestHandler<GetOwnedTenantsQuery, IReadOnlyList<OwnedTenantDto>>>(
            new StubOwnedTenantsHandler(OwnedTenants));

        services.AddSingleton<RecordingSmsService>();
        services.AddSingleton<ISmsService>(sp => sp.GetRequiredService<RecordingSmsService>());

        Services = services.BuildServiceProvider();
        _httpContextAccessor.HttpContext = new DefaultHttpContext { RequestServices = Services };

        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationIdentityDbContext>();
        await db.Database.EnsureCreatedAsync();

        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
        foreach (var role in RoleConstants.GetAllRoles())
        {
            await roles.CreateAsync(new ApplicationRole(role, role));
        }
    }

    public async Task DisposeAsync()
    {
        await Services.DisposeAsync();
        await _container.DisposeAsync();
    }

    public const string Password = "Passw0rd!";

    /// <summary>Tenants the stubbed Tenancy module reports as owned, per user.</summary>
    public System.Collections.Concurrent.ConcurrentDictionary<Guid, IReadOnlyList<OwnedTenantDto>> OwnedTenants { get; } = new();

    public AsyncServiceScope NewScope() => Services.CreateAsyncScope();

    public async Task<ApplicationUser> CreateUserAsync(string? phone = null, params string[] roles)
    {
        await using var scope = NewScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = $"user{Guid.NewGuid():N}",
            Email = $"{Guid.NewGuid():N}@example.com",
            PhoneNumber = phone,
            IsActive = true
        };

        var result = await users.CreateAsync(user, Password);
        result.Succeeded.Should().BeTrue(string.Join(",", result.Errors.Select(e => e.Description)));

        foreach (var role in roles.DefaultIfEmpty(RoleConstants.User))
        {
            await users.AddToRoleAsync(user, role);
        }

        return user;
    }
}

/// <summary>One shared HttpContext (the default accessor is AsyncLocal and would be empty in the test's flow).</summary>
public sealed class FixedHttpContextAccessor : IHttpContextAccessor
{
    public HttpContext? HttpContext { get; set; }
}

public sealed class StubOwnedTenantsHandler(
    System.Collections.Concurrent.ConcurrentDictionary<Guid, IReadOnlyList<OwnedTenantDto>> owned)
    : IRequestHandler<GetOwnedTenantsQuery, IReadOnlyList<OwnedTenantDto>>
{
    public Task<IReadOnlyList<OwnedTenantDto>> Handle(GetOwnedTenantsQuery request, CancellationToken cancellationToken) =>
        Task.FromResult(owned.TryGetValue(request.UserId, out var tenants) ? tenants : []);
}

public sealed class RecordingSmsService : ISmsService
{
    public List<(string Phone, string Code)> Sent { get; } = new();

    public Task SendVerificationCodeAsync(string phoneNumber, string code)
    {
        lock (Sent)
        {
            Sent.Add((phoneNumber, code));
        }

        return Task.CompletedTask;
    }
}

[CollectionDefinition(Name)]
public sealed class IdentityPostgresCollection : ICollectionFixture<IdentityPostgresFixture>
{
    public const string Name = "identity-postgres";
}
