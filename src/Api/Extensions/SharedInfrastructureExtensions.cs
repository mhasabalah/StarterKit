using Api.Infrastructure;
using Carter;
using Figgle.Fonts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http.Timeouts;
using ServiceDefaults;
using Shared.Core.Authorization;
using Shared.Core.Entitlements;
using Shared.Core.Extensions;
using Shared.Core.FeatureFlags;
using Shared.Core.Localization.Extensions;
using Shared.Core.OpenApi;
using Shared.Core.Pagination;
using Shared.Core.Phone;
using Shared.Core.ProblemDetails;
using Shared.Core.Web;
using Shared.Core.BotProtection;
using Shared.Core.Web.Idempotency;
using static Shared.Core.OpenTelemetryCollector.TelemetryTags.Tracing;

namespace Api.Extensions;

public static class SharedInfrastructureExtensions
{
    public static WebApplicationBuilder AddSharedInfrastructure(this WebApplicationBuilder builder)
    {
        // Runs after AddDockerSecrets, so secrets/env vars have already replaced the placeholders.
        builder.EnsureProductionReadyConfiguration();

        // ASPNETCORE_FORWARDEDHEADERS_ENABLED=true would trust X-Forwarded-For from ANY peer (spoofable client IP).
        // Forwarding is handled by UseTrustedForwardedHeaders with an explicit proxy allow-list instead.
        if (TrustedForwardedHeadersExtensions.DisableTrustAllForwardedHeadersFlag(builder.Configuration))
        {
            Console.WriteLine(
                "WARNING: ASPNETCORE_FORWARDEDHEADERS_ENABLED was ignored (it trusts every proxy). " +
                "Configure ForwardedHeaders:KnownProxies / KnownNetworks / TrustPrivateNetworks instead.");
        }

        var appOptions = builder.Services.GetOptions<AppOptions>(nameof(AppOptions));
        Console.WriteLine(FiggleFonts.Standard.Render(appOptions.Name));

        builder.AddServiceDefaults();

        builder.Services.AddHttpContextAccessor();

        builder.Services.AddRedisCache(builder.Configuration);
        builder.Services.AddIdempotency(builder.Configuration);
        builder.Services.AddBotProtection(builder.Configuration); // CAPTCHA port; NoOp unless BotProtection:Enabled
        builder.Services.AddEntitlements();
        builder.Services.AddFeatureFlags(); // config-backed IFeatureFlags; the Tenancy module replaces it with the stored one

        // HTTP Context Services
        builder.Services.AddScoped<ICurrentUserProvider, CurrentUserProvider>();
        builder.Services.AddScoped<IHttpContextService, HttpContextService>();

        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddControllers();
        builder.Services.AddAspnetOpenApi();
        builder.Services.AddCustomVersioning();

        builder.Services.Configure<ApiBehaviorOptions>(options =>
            options.SuppressModelStateInvalidFilter = true);

        // Single exception handler (GlobalExceptionHandler) + status-code pages → ApiResponse error envelope.
        builder.Services.AddCustomProblemDetails();

        // Paging limits (Paging:MaxPageSize / DefaultPageSize / AnonymousMaxPageSize) used by the shared paging helpers.
        builder.Services.AddPaging(builder.Configuration);

        builder.Services.AddAppLocalization();

        // Register Memory Cache (required by EnumLookupService and other caching services)
        builder.Services.AddMemoryCache();

        // Shared authorization policies
        builder.Services.AddSingleton<IAuthorizationHandler, PageGateAuthorizationHandler>();
        builder.Services.AddAuthorization(options =>
        {
            // Page 1 is public (anonymous pageSize capped by Paging:AnonymousMaxPageSize); page 2+ requires authentication.
            // Apply with .RequireAuthorization("PageGate") on anonymous list/search endpoints.
            options.AddPolicy("PageGate",
                p => p.AddRequirements(new PageGateRequirement()));
        });

        builder.Services.AddPhoneValidationFeature();

        builder.Services.AddConfigurableCors(builder.Configuration, builder.Environment);

        builder.Services.AddConfiguredRequestTimeouts(builder.Configuration);

        // Register Carter for Api assembly + Shared.Core (phone endpoint lives in Api.Endpoints)
        builder.Services.AddCarterWithAssemblies(typeof(Program).Assembly);

        return builder;
    }

    /// <summary>
    /// Request timeouts: <c>RequestTimeouts:DefaultSeconds</c> (default 30; 0 = no default timeout) applies to every
    /// endpoint; <see cref="RequestTimeoutPolicies.LongRunning"/> (<c>LongRunningSeconds</c>, default 300) is for
    /// file upload/download. Timeouts cancel <c>HttpContext.RequestAborted</c> and answer 504.
    /// </summary>
    public static IServiceCollection AddConfiguredRequestTimeouts(this IServiceCollection services,
        IConfiguration configuration)
    {
        var defaultSeconds = configuration.GetValue("RequestTimeouts:DefaultSeconds", 30);
        var longRunningSeconds = configuration.GetValue("RequestTimeouts:LongRunningSeconds", 300);

        services.AddRequestTimeouts(options =>
        {
            if (defaultSeconds > 0)
            {
                options.DefaultPolicy = new RequestTimeoutPolicy
                {
                    Timeout = TimeSpan.FromSeconds(defaultSeconds),
                    TimeoutStatusCode = StatusCodes.Status504GatewayTimeout
                };
            }

            options.AddPolicy(RequestTimeoutPolicies.LongRunning, new RequestTimeoutPolicy
            {
                Timeout = longRunningSeconds > 0 ? TimeSpan.FromSeconds(longRunningSeconds) : Timeout.InfiniteTimeSpan,
                TimeoutStatusCode = StatusCodes.Status504GatewayTimeout
            });
        });

        return services;
    }

    /// <summary>
    /// Registers correlation id + response wrapping + exception/status-code handling middleware.
    /// Must be called BEFORE UseIdentityModule so that 401/403 from auth middleware
    /// pass through UseStatusCodePages and get proper JSON bodies.
    /// </summary>
    public static WebApplication UseErrorHandling(this WebApplication app)
    {
        // First, so the correlation id is in the logging scope of everything below (incl. exception logs)
        // and echoed on every response, errors included.
        app.UseCorrelationId();
        app.UseResponseWrapper();
        app.UseCustomProblemDetails();
        return app;
    }

    public static WebApplication UseSharedInfrastructure(this WebApplication app)
    {
        var appOptions = app.Configuration.GetOptions<AppOptions>(nameof(AppOptions));

        // Fingerprints request bodies of .WithIdempotency() endpoints (must run before the endpoint binds the body).
        app.UseIdempotency();

        // The marker filter flags endpoints that already return an ApiResponse envelope (see ResponseWrapperMiddleware).
        // Deny by default: a Carter endpoint that declares no authorization requires an authenticated user.
        app.WithApiResponseMarker().RequireAuthorizationByDefault().MapCarter();

        // Map health check endpoints
        app.UseServiceDefaults();

        app.UseAppLocalization();

        app.MapGet("/", x => x.Response.WriteAsync(appOptions.Name));

        if (app.Environment.IsDevelopment())
        {
            app.UseAspnetOpenApi();
        }

        return app;
    }
}
