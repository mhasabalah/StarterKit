using Api.Infrastructure;
using Carter;
using Figgle.Fonts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ServiceDefaults;
using Shared.Core.Authorization;
using Shared.Core.Extensions;
using Shared.Core.Localization.Extensions;
using Shared.Core.OpenApi;
using Shared.Core.Pagination;
using Shared.Core.Phone;
using Shared.Core.ProblemDetails;
using Shared.Core.Web;
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

        // Register Carter for Api assembly + Shared.Core (phone endpoint lives in Api.Endpoints)
        builder.Services.AddCarterWithAssemblies(typeof(Program).Assembly);

        return builder;
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

        // The marker filter flags endpoints that already return an ApiResponse envelope (see ResponseWrapperMiddleware).
        app.WithApiResponseMarker().MapCarter();

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
