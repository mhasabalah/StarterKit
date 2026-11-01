using Humanizer;
using Identity.Data.Seed;
using Identity.Data.Repository.Onboarding;
using Identity.Domains.Onboarding.Jobs;
using Identity.Infrastructure;
using Identity.Infrastructure.BackgroundJobs;
using Identity.Services.Token;
using Shared.Core.Authorization;
using Shared.Core.Extensions;
using Shared.Core.Mapster;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Identity.Options;
using RoleConstants = Identity.Domains.Roles.Constants.RoleConstants;

namespace Identity;

public static class IdentityModule
{
    public static IServiceCollection AddIdentityModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Configure Options Pattern
        services.ConfigureOptions(configuration);

        // 1. Register DbContext
        services.AddCustomDbContext<ApplicationIdentityDbContext>(configuration, "starterkit");

        // 2. Configure ASP.NET Core Identity
        services.ConfigureIdentity();

        // 3. Configure JWT Authentication
        services.ConfigureJwtAuthentication(configuration);

        // 4. Configure External Authentication
        services.ConfigureExternalAuthentication(configuration);

        // 5. Configure Authorization Policies
        services.ConfigureAuthorizationPolicies();

        // 6. Register services
        services.AddScoped<IJwtTokenGenerator, JwtTokenGenerator>();
        services.AddScoped<IDataSeeder, IdentityDataSeeder>();
        services.ConfigureSmsService();
        services.Configure<OnboardingOptions>(configuration.GetSection("Notification:Onboarding"));
        services.AddScoped<IOnboardingProcessRepository, OnboardingProcessRepository>();
        services.AddTransient<OnboardingTimeoutJob>();
        services.AddTransient<OnboardingOtpRetryJob>();

        // Token Blacklist Service (for logout) - Using shared Redis from Shared.Core
        services.AddSingleton<ITokenBlacklistService, TokenBlacklistService>();
        services.AddScoped<TokenBlacklistJwtBearerEvents>();

        // 7. Register background jobs
        services.AddScoped<RefreshTokenCleanupJob>();
        services.AddHostedService<IdentityJobSchedulerBootstrap>();

        // 8. Add MediatR, Carter
        services.AddMediatR(IdentityRoot.AssemblyName);
        services.AddCarterWithAssemblies(IdentityRoot.AssemblyName);

        return services;
    }

    private static void ConfigureOptions(this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.Configure<Options.AuthenticationOptions>(
            configuration.GetSection(Options.AuthenticationOptions.SectionName));
        services.Configure<Options.IdentityOptions>(
            configuration.GetSection(Options.IdentityOptions.SectionName));
        services.Configure<PhoneVerificationOptions>(
            configuration.GetSection(PhoneVerificationOptions.SectionName));
        services.Configure<SmsOptions>(configuration.GetSection(SmsOptions.SectionName));
        services.Configure<LoginPolicyOptions>(configuration.GetSection(LoginPolicyOptions.SectionName));
        services.Configure<ApplicationOptions>(configuration.GetSection(ApplicationOptions.SectionName));
        services.Configure<PasswordResetOptions>(configuration.GetSection(PasswordResetOptions.SectionName));
    }

    private static void ConfigureIdentity(this IServiceCollection services)
    {
        var serviceProvider = services.BuildServiceProvider();
        var identityOptions = serviceProvider.GetService<IOptions<Options.IdentityOptions>>()?.Value
                              ?? new Options.IdentityOptions();

        services.AddIdentity<ApplicationUser, ApplicationRole>(options =>
            {
                // Password settings
                options.Password.RequireDigit = identityOptions.Password.RequireDigit;
                options.Password.RequiredLength = identityOptions.Password.RequiredLength;
                options.Password.RequireNonAlphanumeric =
                    identityOptions.Password.RequireNonAlphanumeric;
                options.Password.RequireUppercase = identityOptions.Password.RequireUppercase;
                options.Password.RequireLowercase = identityOptions.Password.RequireLowercase;
                options.Password.RequiredUniqueChars = identityOptions.Password.RequiredUniqueChars;

                // Lockout settings
                options.Lockout.DefaultLockoutTimeSpan =
                    TimeSpan.FromMinutes(identityOptions.Lockout.DefaultLockoutTimeSpanMinutes);
                options.Lockout.MaxFailedAccessAttempts =
                    identityOptions.Lockout.MaxFailedAccessAttempts;
                options.Lockout.AllowedForNewUsers = identityOptions.Lockout.AllowedForNewUsers;

                // User settings
                options.User.RequireUniqueEmail = identityOptions.User.RequireUniqueEmail;
                options.User.AllowedUserNameCharacters =
                    identityOptions.User.AllowedUserNameCharacters;

                // SignIn settings
                options.SignIn.RequireConfirmedEmail = identityOptions.SignIn.RequireConfirmedEmail;
                options.SignIn.RequireConfirmedPhoneNumber =
                    identityOptions.SignIn.RequireConfirmedPhoneNumber;
            })
            .AddEntityFrameworkStores<ApplicationIdentityDbContext>()
            .AddDefaultTokenProviders();
    }

    private static void ConfigureJwtAuthentication(this IServiceCollection services,
        IConfiguration configuration)
    {
        var serviceProvider = services.BuildServiceProvider();
        var jwtOptions = serviceProvider.GetService<IOptions<JwtOptions>>()?.Value;

        if (jwtOptions == null)
        {
            throw new InvalidOperationException("JWT settings not configured properly");
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SecretKey));

        services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options =>
            {
                options.SaveToken = true;
                options.RequireHttpsMetadata = jwtOptions.RequireHttpsMetadata;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = jwtOptions.ValidateIssuer,
                    ValidateAudience = jwtOptions.ValidateAudience,
                    ValidateLifetime = jwtOptions.ValidateLifetime,
                    ValidateIssuerSigningKey = jwtOptions.ValidateIssuerSigningKey,
                    ValidIssuer = jwtOptions.Issuer,
                    ValidAudience = jwtOptions.Audience,
                    IssuerSigningKey = key,
                    ClockSkew = TimeSpan.Zero
                };

                // Configure token blacklist validation
                options.EventsType = typeof(TokenBlacklistJwtBearerEvents);
            });
    }

    private static void ConfigureExternalAuthentication(this IServiceCollection services,
        IConfiguration configuration)
    {
        var serviceProvider = services.BuildServiceProvider();
        var authOptions =
            serviceProvider.GetService<IOptions<Options.AuthenticationOptions>>()?.Value
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

    private static void ConfigureAuthorizationPolicies(this IServiceCollection services)
    {
        services.AddAuthorization(options =>
        {
            options.AddPolicy(PolicyConstants.UserOnly, policy =>
                policy.RequireAuthenticatedUser());

            options.AddPolicy(PolicyConstants.AdminOnly, policy =>
                policy.RequireRole(RoleConstants.Admin, RoleConstants.SuperAdmin));

            options.AddPolicy(PolicyConstants.SuperAdminOnly, policy =>
                policy.RequireRole(RoleConstants.SuperAdmin));

            options.AddPolicy(PolicyConstants.ProfileComplete, policy =>
                policy.Requirements.Add(new Authorization.ProfileCompleteRequirement()));
        });

        services.AddScoped<IAuthorizationHandler, Authorization.ProfileCompleteHandler>();
    }

    private static void ConfigureSmsService(this IServiceCollection services)
    {
        var serviceProvider = services.BuildServiceProvider();
        var smsOptions = serviceProvider.GetService<IOptions<SmsOptions>>()?.Value
                         ?? new SmsOptions();

        switch (smsOptions.Provider)
        {
            case SmsProvider.Mock:
                services.AddScoped<ISmsService, MockSmsService>();
                break;
            case SmsProvider.Twilio:
                // TODO: Implement TwilioSmsService
                services.AddScoped<ISmsService, MockSmsService>();
                break;
            case SmsProvider.AwsSns:
                // TODO: Implement AwsSnsSmsService
                services.AddScoped<ISmsService, MockSmsService>();
                break;
            default:
                services.AddScoped<ISmsService, MockSmsService>();
                break;
        }
    }

    public static IApplicationBuilder UseIdentityModule(this IApplicationBuilder app)
    {
        // Use authentication & authorization
        app.UseAuthentication();
        app.UseAuthorization();

        // Apply migrations and seed data (migrations FIRST, then seeding)
        app.UseMigration<ApplicationIdentityDbContext>();
        app.UseMiddleware<MustChangePasswordMiddleware>();

        return app;
    }
}
