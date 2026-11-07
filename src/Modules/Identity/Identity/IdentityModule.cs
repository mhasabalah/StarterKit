using Identity.Authorization;
using Identity.Data.Repository.ApiKeys;
using Identity.Data.Repository.Otp;
using Identity.Data.Repository.RefreshTokens;
using Identity.Data.Seed;
using Identity.Domains.Audit.Services;
using Identity.Domains.Auth.Services;
using Identity.Domains.Onboarding.Jobs;
using Identity.Domains.Users.Services;
using Identity.Infrastructure;
using Identity.Infrastructure.BackgroundJobs;
using Identity.Infrastructure.DataProtection;
using Identity.Options;
using Identity.Services.ApiKeys;
using Identity.Services.EmailConfirmation;
using Identity.Services.Otp;
using Identity.Services.Passkeys;
using Identity.Services.Security;
using Identity.Services.Sso;
using Identity.Services.Scim;
using Identity.Data.Repository.Sso;
using Identity.Domains.Sso.Features.SsoConnections;
using Identity.Domains.Sso.Features.SsoLogin;
using Identity.Services.Phone;
using Identity.Services.Sessions;
using Identity.Services.Token;
using Identity.Services.TwoFactor;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Shared.Core.Authorization;
using Shared.Core.Messaging.Outbox;
using AspNetIdentityOptions = Microsoft.AspNetCore.Identity.IdentityOptions;
using RoleConstants = Identity.Domains.Roles.Constants.RoleConstants;

namespace Identity;

public static class IdentityModule
{
    public static IServiceCollection AddIdentityModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);

        services.ConfigureOptions(configuration);
        services.AddCustomDbContext<ApplicationIdentityDbContext>(configuration, "starterkit");
        services.AddOutbox<ApplicationIdentityDbContext>(); // delivers UserDeletedIntegrationEvent, UserSessionsRevokedIntegrationEvent
        services.ConfigureIdentity();
        // Data Protection key ring (encrypts authenticator keys, signs reset tokens): database-backed, shared
        // application name, optional at-rest encryption (Identity:DataProtection) + production guard.
        services.AddIdentityDataProtection(configuration);
        services.ConfigureJwtAuthentication();
        services.ConfigureExternalAuthentication(configuration);
        services.AddIdentityAuthorizationPolicies(configuration);
        services.ConfigureSmsService(configuration);

        // Sessions, tokens, OTP
        services.AddScoped<IJwtTokenGenerator, JwtTokenGenerator>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IAuthSessionService, AuthSessionService>();
        services.AddScoped<ISessionRevoker, SessionRevoker>();
        services.AddScoped<ISessionManager, SessionManager>();
        services.AddScoped<ISessionRevocationRecorder, SessionRevocationRecorder>();
        services.AddSingleton<Shared.Core.Authorization.ISessionRevocationChecker, SessionRevocationChecker>();
        services.AddScoped<IApiKeyRepository, ApiKeyRepository>();
        services.AddScoped<IApiKeyAuthenticator, ApiKeyAuthenticator>();
        services.AddSingleton<ITokenBlacklistService, TokenBlacklistService>();
        services.AddScoped<TokenBlacklistJwtBearerEvents>();
        services.AddScoped<IOtpCodeRepository, OtpCodeRepository>();
        services.AddScoped<IOtpCodeService, OtpCodeService>();
        services.AddScoped<IOtpSender, OtpSender>();
        services.AddScoped<IEmailConfirmationSender, EmailConfirmationSender>();

        // Two-factor (TOTP) + login completion
        services.AddScoped<ITwoFactorService, TwoFactorService>();
        services.AddSingleton<ITotpReplayGuard>(CreateTotpReplayGuard);
        services.AddScoped<ITwoFactorChallengeService, TwoFactorChallengeService>();
        services.AddScoped<ILoginCompletionService, LoginCompletionService>();

        // Passkeys (WebAuthn): built-in Identity handler + server-side single-use ceremony state.
        services.AddHttpContextAccessor();
        services.TryAddScoped<IPasskeyHandler<ApplicationUser>, PasskeyHandler<ApplicationUser>>();
        services.AddSingleton<IPasskeyCeremonyStore, DistributedCachePasskeyCeremonyStore>();
        services.AddScoped<IPasskeyService, PasskeyService>();
        services.AddScoped<ISecurityAlertSender, SecurityAlertSender>();

        // Enterprise SSO (per-tenant OIDC / SAML 2.0) - see wiki/Identity/Enterprise-SSO.md.
        services.AddSsoHttpClient();
        Shared.Core.Net.DnsTxtVerifierServiceCollectionExtensions.AddDnsTxtVerifier(services);
        services.AddSingleton<IOneTimeUseGuard>(CreateOneTimeUseGuard);
        services.AddSingleton<OidcConfigurationCache>();
        services.AddScoped<ISsoSecretProtector, SsoSecretProtector>();
        services.AddScoped<ISsoConnectionRepository, SsoConnectionRepository>();
        services.AddScoped<ITenantRoleGuard, TenantRoleGuard>();
        services.AddScoped<ISsoEntitlement, SsoEntitlement>();
        services.AddScoped<Identity.Data.Repository.Scim.IScimDirectoryRepository, Identity.Data.Repository.Scim.ScimDirectoryRepository>();
        services.AddScoped<ISsoEnforcementPolicy, SsoEnforcementPolicy>();
        services.AddScoped<SsoOneTimeStore>();
        services.AddScoped<SsoUrls>();
        services.AddScoped<SsoReturnUrlPolicy>();
        services.AddScoped<OidcSsoClient>();
        services.AddScoped<SamlSsoClient>();
        services.AddScoped<SsoAccountService>();
        services.AddScoped<SsoLoginFlow>();
        services.AddScoped<SsoConnectionMapper>();

        // SCIM 2.0 provisioning (directory bearer tokens; own scheme + policy) - see wiki/Identity/SCIM.md.
        services.AddScimAuthentication();
        services.AddScoped<Identity.Services.Scim.ScimProvisioningService>();
        services.AddScoped<Identity.Domains.Scim.Features.ScimProtocol.ScimDirectoryLoader>();
        services.AddScoped<Identity.Domains.Scim.Features.ScimDirectories.ScimDirectoryMapper>();
        services.AddScoped<Identity.Domains.Scim.Features.ScimProtocol.ScimConflictRetry>();

        // Admin audit: writer + one transaction per IAuditedCommand (registered after AddMediator below).
        services.AddScoped<IIdentityAuditWriter, IdentityAuditWriter>();
        services.AddScoped<IIdentityUnitOfWork, IdentityUnitOfWork>();

        // Users / admins
        services.AddScoped<IPhoneNumberNormalizer, PhoneNumberNormalizer>();
        services.AddScoped<IRoleHierarchyGuard, RoleHierarchyGuard>();
        services.AddScoped<IUserContactService, UserContactService>();

        // Onboarding + seeding
        services.AddScoped<IDataSeeder, IdentityDataSeeder>();
        services.AddScoped<IOnboardingProcessRepository, OnboardingProcessRepository>();
        services.AddTransient<OnboardingTimeoutJob>();
        services.AddTransient<OnboardingOtpRetryJob>();

        // Background jobs
        services.AddScoped<RefreshTokenCleanupJob>();
        services.AddHostedService<IdentityJobSchedulerBootstrap>();

        services.AddMediator(IdentityRoot.AssemblyName);
        services.TryAddEnumerable(ServiceDescriptor.Transient(
            typeof(IPipelineBehavior<,>), typeof(AuditedCommandTransactionBehavior<,>)));
        services.AddCarterWithAssemblies(IdentityRoot.AssemblyName);

        // Impersonated sessions may only READ Identity endpoints (never change credentials, 2FA, sessions, profile).
        services.Configure<Shared.Core.MultiTenancy.Web.ImpersonationGuardOptions>(
            o => o.ReadOnlyAssemblies.Add(IdentityRoot.AssemblyName));

        return services;
    }

    /// <summary>
    /// Redis (SET NX, atomic across instances) when Redis is enabled - <c>AddRedisCache</c> then registers the shared
    /// <see cref="StackExchange.Redis.IConnectionMultiplexer"/>; otherwise the in-process IDistributedCache fallback.
    /// </summary>
    private static ITotpReplayGuard CreateTotpReplayGuard(IServiceProvider sp)
    {
        var redis = sp.GetService<StackExchange.Redis.IConnectionMultiplexer>();
        if (redis is not null)
        {
            var prefix = sp.GetService<IOptions<Shared.Core.Options.RedisOptions>>()?.Value.InstanceName ?? string.Empty;
            return new RedisTotpReplayGuard(redis, prefix);
        }

        return new DistributedCacheTotpReplayGuard(
            sp.GetRequiredService<Microsoft.Extensions.Caching.Distributed.IDistributedCache>());
    }

    /// <summary>Same Redis-or-fallback choice as the TOTP replay guard.</summary>
    private static IOneTimeUseGuard CreateOneTimeUseGuard(IServiceProvider sp)
    {
        var redis = sp.GetService<StackExchange.Redis.IConnectionMultiplexer>();
        if (redis is not null)
        {
            var prefix = sp.GetService<IOptions<Shared.Core.Options.RedisOptions>>()?.Value.InstanceName ?? string.Empty;
            return new RedisOneTimeUseGuard(redis, prefix);
        }

        return new DistributedCacheOneTimeUseGuard(
            sp.GetRequiredService<Microsoft.Extensions.Caching.Distributed.IDistributedCache>());
    }

    private static void ConfigureOptions(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<JwtOptions>, JwtOptionsValidator>();

        services.AddOptions<IdentitySecurityOptions>()
            .Bind(configuration.GetSection(IdentitySecurityOptions.SectionName))
            .PostConfigure<IHostEnvironment>((options, environment) =>
            {
                // Development/Testing convenience only: a random per-process key (codes die on restart).
                if (string.IsNullOrEmpty(options.OtpHmacKey) && environment.IsDevelopmentLike())
                {
                    options.OtpHmacKey = Convert.ToBase64String(
                        System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
                }
            })
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<IdentitySecurityOptions>, IdentitySecurityOptionsValidator>();

        services.Configure<ApiKeyOptions>(configuration.GetSection(ApiKeyOptions.SectionName));
        services.Configure<Options.AuthenticationOptions>(
            configuration.GetSection(Options.AuthenticationOptions.SectionName));
        services.Configure<Options.IdentityOptions>(configuration.GetSection(Options.IdentityOptions.SectionName));
        services.Configure<PhoneVerificationOptions>(configuration.GetSection(PhoneVerificationOptions.SectionName));
        services.Configure<SmsOptions>(configuration.GetSection(SmsOptions.SectionName));
        services.Configure<LoginPolicyOptions>(configuration.GetSection(LoginPolicyOptions.SectionName));
        services.Configure<ApplicationOptions>(configuration.GetSection(ApplicationOptions.SectionName));
        services.Configure<PasswordResetOptions>(configuration.GetSection(PasswordResetOptions.SectionName));
        services.Configure<EmailConfirmationOptions>(configuration.GetSection(EmailConfirmationOptions.SectionName));
        services.Configure<OnboardingOptions>(configuration.GetSection("Notification:Onboarding"));
        services.Configure<PasskeyOptions>(configuration.GetSection(PasskeyOptions.SectionName));
        services.Configure<SsoOptions>(configuration.GetSection(SsoOptions.SectionName));
    }

    private static void ConfigureIdentity(this IServiceCollection services)
    {
        services.AddIdentity<ApplicationUser, ApplicationRole>()
            .AddEntityFrameworkStores<ApplicationIdentityDbContext>()
            .AddDefaultTokenProviders()
            .AddTokenProvider<EmailConfirmationTokenProvider>(EmailConfirmationTokenProviderOptions.ProviderName);

        // Same EF store, but the authenticator key is encrypted and recovery codes are stored as hashes.
        services.Replace(ServiceDescriptor.Scoped<IUserStore<ApplicationUser>, ProtectedUserStore>());


        // Options are applied lazily from IOptions (no BuildServiceProvider during registration).
        services.AddOptions<AspNetIdentityOptions>()
            .Configure<IOptions<Options.IdentityOptions>>((options, settings) =>
            {
                var s = settings.Value;

                options.Password.RequireDigit = s.Password.RequireDigit;
                options.Password.RequiredLength = s.Password.RequiredLength;
                options.Password.RequireNonAlphanumeric = s.Password.RequireNonAlphanumeric;
                options.Password.RequireUppercase = s.Password.RequireUppercase;
                options.Password.RequireLowercase = s.Password.RequireLowercase;
                options.Password.RequiredUniqueChars = s.Password.RequiredUniqueChars;

                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(s.Lockout.DefaultLockoutTimeSpanMinutes);
                options.Lockout.MaxFailedAccessAttempts = s.Lockout.MaxFailedAccessAttempts;
                options.Lockout.AllowedForNewUsers = s.Lockout.AllowedForNewUsers;

                options.User.RequireUniqueEmail = s.User.RequireUniqueEmail;
                options.User.AllowedUserNameCharacters = s.User.AllowedUserNameCharacters;

                options.SignIn.RequireConfirmedEmail = s.SignIn.RequireConfirmedEmail;
                options.SignIn.RequireConfirmedPhoneNumber = s.SignIn.RequireConfirmedPhoneNumber;

                options.Tokens.EmailConfirmationTokenProvider = EmailConfirmationTokenProviderOptions.ProviderName;

                // Schema v3 adds the user_passkeys table (WebAuthn credentials).
                options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
            });

        services.AddOptions<IdentityPasskeyOptions>()
            .Configure<IOptions<PasskeyOptions>>((options, settings) => PasskeyConfiguration.Apply(options, settings.Value));

        // The reset link/OTP promise "valid for N hours" must match what Identity actually enforces (default 24h).
        services.AddOptions<DataProtectionTokenProviderOptions>()
            .Configure<IOptions<PasswordResetOptions>>((options, reset) =>
                options.TokenLifespan = TimeSpan.FromHours(Math.Max(1, reset.Value.TokenExpiryHours)));

        // Confirmation links have their own (longer) lifetime: EmailConfirmation:TokenExpiryHours, default 24h.
        services.AddOptions<EmailConfirmationTokenProviderOptions>()
            .Configure<IOptions<EmailConfirmationOptions>>((options, confirm) =>
                options.TokenLifespan = TimeSpan.FromHours(Math.Max(1, confirm.Value.TokenExpiryHours)));
    }

    private static void ConfigureJwtAuthentication(this IServiceCollection services)
    {
        // Default = a policy scheme that forwards to ApiKey when the request carries an API key
        // ("Authorization: ApiKey ..." or "X-Api-Key"), otherwise to Bearer (JWT) as before.
        services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = ApiKeyClaimTypes.PolicySchemeName;
                options.DefaultChallengeScheme = ApiKeyClaimTypes.PolicySchemeName;
                options.DefaultScheme = ApiKeyClaimTypes.PolicySchemeName;
            })
            .AddJwtBearer()
            .AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(ApiKeyClaimTypes.SchemeName, null)
            .AddPolicyScheme(ApiKeyClaimTypes.PolicySchemeName, "Bearer or API key", options =>
            {
                options.ForwardDefault = JwtBearerDefaults.AuthenticationScheme;
                options.ForwardDefaultSelector = ApiKeyRequest.SelectScheme;
            });

        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((options, jwtOptions) =>
            {
                var jwt = jwtOptions.Value;

                options.SaveToken = true;
                options.RequireHttpsMetadata = jwt.RequireHttpsMetadata;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    RequireExpirationTime = true,
                    RequireSignedTokens = true,
                    ValidIssuer = jwt.Issuer,
                    ValidAudience = jwt.Audience,
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    IssuerSigningKeys = JwtSigningKeys.ValidationKeys(jwt),
                    ClockSkew = TimeSpan.Zero
                };

                options.EventsType = typeof(TokenBlacklistJwtBearerEvents);
            });
    }

    private static void ConfigureExternalAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        // Which schemes exist must be known at registration time, so read the section directly.
        var authOptions = configuration.GetSection(Options.AuthenticationOptions.SectionName)
                              .Get<Options.AuthenticationOptions>()
                          ?? new Options.AuthenticationOptions();

        var authBuilder = services.AddAuthentication();

        if (authOptions.Google.Enabled && !string.IsNullOrEmpty(authOptions.Google.ClientId))
        {
            authBuilder.AddGoogle(options =>
            {
                options.ClientId = authOptions.Google.ClientId;
                options.ClientSecret = authOptions.Google.ClientSecret;
                options.SaveTokens = authOptions.SaveTokens;
            });
        }

        if (authOptions.Facebook.Enabled && !string.IsNullOrEmpty(authOptions.Facebook.AppId))
        {
            authBuilder.AddFacebook(options =>
            {
                options.AppId = authOptions.Facebook.AppId;
                options.AppSecret = authOptions.Facebook.AppSecret;
                options.SaveTokens = authOptions.SaveTokens;
            });
        }
    }

    private static void ConfigureSmsService(this IServiceCollection services, IConfiguration configuration)
    {
        var provider = configuration.GetSection(SmsOptions.SectionName).Get<SmsOptions>()?.Provider
                       ?? SmsProvider.Mock;

        switch (provider)
        {
            case SmsProvider.Mock:
                // The mock is for Development/Testing only. Elsewhere sending fails loudly instead of
                // pretending to deliver codes nobody receives.
                services.AddScoped<ISmsService>(sp =>
                    sp.GetRequiredService<IHostEnvironment>().IsDevelopmentLike()
                        ? ActivatorUtilities.CreateInstance<MockSmsService>(sp)
                        : new DisabledSmsService());
                break;
            default:
                throw new InvalidOperationException(
                    $"SmsSettings:Provider '{provider}' is configured but has no implementation. " +
                    "Implement ISmsService for it or use 'Mock' in Development.");
        }
    }

    /// <param name="app">The application.</param>
    /// <param name="afterAuthentication">
    /// Middleware that must run between authentication and authorization - the tenant resolution middleware
    /// (needs the authenticated user; tenant permission policies need the resolved membership).
    /// </param>
    public static IApplicationBuilder UseIdentityModule(this IApplicationBuilder app,
        Action<IApplicationBuilder>? afterAuthentication = null)
    {
        app.UseAuthentication();
        app.UseMiddleware<ApiKeyRestrictionMiddleware>(); // DisallowApiKey() endpoints: 403 for API-key callers
        afterAuthentication?.Invoke(app);
        app.UseAuthorization();

        // Migrations first, then seeding (inside UseMigration).
        app.UseMigration<ApplicationIdentityDbContext>();
        app.UseMiddleware<MustChangePasswordMiddleware>();

        return app;
    }
}
