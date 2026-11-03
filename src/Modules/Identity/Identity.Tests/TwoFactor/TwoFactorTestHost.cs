using Identity.Data;
using Identity.Domains.Audit.Services;
using Identity.Domains.Auth.Services;
using Identity.Domains.Roles.Models;
using Identity.Domains.Users.Models;
using Identity.Options;
using Identity.Services.Sessions;
using Identity.Services.TwoFactor;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shared.Core.Web;

namespace Identity.Tests.TwoFactor;

/// <summary>
/// Real ASP.NET Identity (UserManager + authenticator token provider + <see cref="ProtectedUserStore"/>) on the EF
/// InMemory provider, real 2FA/challenge/login-completion/audit services, a fixed clock, and mocks only at the edges
/// (session issuance, session revocation, HTTP context).
/// </summary>
public sealed class TwoFactorTestHost : IAsyncDisposable
{
    public const string Password = "Passw0rd!";

    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero));
    public Mock<IAuthSessionService> Sessions { get; } = new();
    public Mock<ISessionRevoker> Revoker { get; } = new();
    public Mock<IHttpContextService> Http { get; } = new();
    public ServiceProvider Services { get; }

    private readonly AsyncServiceScope _scope;

    public TwoFactorTestHost(int maxFailedAccessAttempts = 3)
    {
        var databaseName = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddSingleton<TimeProvider>(Clock);

        services.AddDbContext<ApplicationIdentityDbContext>(o => o
            .UseInMemoryDatabase(databaseName)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)));

        services.AddIdentityCore<ApplicationUser>(o =>
            {
                o.Lockout.MaxFailedAccessAttempts = maxFailedAccessAttempts;
                o.Lockout.AllowedForNewUsers = true;
                o.Password.RequireNonAlphanumeric = false;
            })
            .AddRoles<ApplicationRole>()
            .AddEntityFrameworkStores<ApplicationIdentityDbContext>()
            .AddDefaultTokenProviders();
        services.Replace(ServiceDescriptor.Scoped<IUserStore<ApplicationUser>, ProtectedUserStore>());

        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(new JwtOptions
        {
            SecretKey = "unit-test-jwt-secret-key-0123456789abcdef", Issuer = "issuer", Audience = "audience",
            AccessTokenExpirationMinutes = 15, RefreshTokenExpirationDays = 7
        }));
        services.Configure<TwoFactorOptions>(o => o.RecoveryCodeCount = 8);
        services.Configure<LoginPolicyOptions>(_ => { });

        Sessions.Setup(s => s.IssueAsync(It.IsAny<ApplicationUser>(), It.IsAny<string?>(), It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((ApplicationUser _, string? _, bool mfa, CancellationToken _) =>
                new AuthSession("access", "refresh", DateTime.UtcNow.AddMinutes(15), [], mfa));
        Sessions.Setup(s => s.IssueAsync(It.IsAny<ApplicationUser>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AuthSession("access", "refresh", DateTime.UtcNow.AddMinutes(15), []));
        Http.Setup(h => h.GetIpAddress()).Returns("127.0.0.1");

        services.AddSingleton(Sessions.Object);
        services.AddSingleton(Revoker.Object);
        services.AddSingleton(Http.Object);
        services.AddScoped<ITwoFactorService, TwoFactorService>();
        services.AddDistributedMemoryCache();
        services.AddSingleton<ITotpReplayGuard>(sp =>
            new DistributedCacheTotpReplayGuard(sp.GetRequiredService<Microsoft.Extensions.Caching.Distributed.IDistributedCache>()));
        services.AddScoped<ITwoFactorChallengeService, TwoFactorChallengeService>();
        services.AddScoped<ILoginCompletionService, LoginCompletionService>();
        services.AddScoped<IIdentityAuditWriter, IdentityAuditWriter>();
        services.AddScoped<IIdentityUnitOfWork, IdentityUnitOfWork>();

        Services = services.BuildServiceProvider();
        _scope = Services.CreateAsyncScope();
    }

    public IServiceProvider Scope => _scope.ServiceProvider;
    public UserManager<ApplicationUser> Users => Scope.GetRequiredService<UserManager<ApplicationUser>>();
    public ApplicationIdentityDbContext Db => Scope.GetRequiredService<ApplicationIdentityDbContext>();

    public T Create<T>() => ActivatorUtilities.CreateInstance<T>(Scope);

    public async Task<ApplicationUser> CreateUserAsync()
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = $"u{Guid.NewGuid():N}",
            Email = $"{Guid.NewGuid():N}@example.com",
            IsActive = true
        };
        var result = await Users.CreateAsync(user, Password);
        result.Succeeded.Should().BeTrue(string.Join(",", result.Errors.Select(e => e.Code)));
        return user;
    }

    /// <summary>Reads a user fresh from a NEW scope (what was really saved).</summary>
    public async Task<ApplicationUser> ReloadAsync(Guid id)
    {
        await using var scope = Services.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByIdAsync(id.ToString()))!;
    }

    public async Task<string?> RawTokenAsync(Guid userId, string name)
    {
        await using var scope = Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ApplicationIdentityDbContext>()
            .UserTokens.AsNoTracking()
            .Where(t => t.UserId == userId && t.LoginProvider == ProtectedUserStore.InternalLoginProvider && t.Name == name)
            .Select(t => t.Value)
            .SingleOrDefaultAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _scope.DisposeAsync();
        await Services.DisposeAsync();
    }
}
