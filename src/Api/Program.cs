using Api.Extensions;
using Api.Infrastructure;
using Identity;
using Tenancy;
using Tenancy.Services;
using Shared.Core.Extensions;
using Shared.Messaging.Extensions;
using Shared.Core.Web;
using Aspire.Seq;
using Hangfire;
using Hangfire.Dashboard;
using Shared.Core.Data;
using Shared.Core.MultiTenancy;
using Shared.Core.MultiTenancy.Web;

var builder = WebApplication.CreateBuilder(args);

// One-shot migrator run of the same image: `dotnet Api.dll --migrate-only` migrates + seeds every database, then exits.
var migrateOnly = args.Contains(DatabaseMigrationOptions.MigrateOnlyArgument);
if (migrateOnly)
{
    builder.Configuration[$"{DatabaseMigrationOptions.SectionName}:{nameof(DatabaseMigrationOptions.MigrateOnly)}"] = "true";
}

// Add Docker Secrets support (reads from /run/secrets/ if available)
builder.Configuration.AddDockerSecrets();

var configuration = builder.Configuration;

builder.AddSharedInfrastructure();

// Multi-tenancy kernel (MultiTenancy section; disabled = single tenant / Default tenant). Before the modules.
builder.Services.AddMultiTenancy(configuration);

// Add Hangfire
builder.Services.AddHangfireServices(configuration);

// Add Rate Limiting — all policies configured via RateLimiting:Policies[] in appsettings
builder.Services.AddConfigurableRateLimiting(configuration);

// Log/trace shipping to Seq (ConnectionStrings:seq). No health check: Seq is a telemetry sink, and its outage must not
// mark the API "not ready" (/health = the API's own dependencies: Postgres, Redis).
builder.AddSeqEndpoint("seq", seq => seq.DisableHealthChecks = true);

// Modules (ADR-0019): discovered from the module ProjectReferences of Api.csproj, registered in ModuleOrder.
// Also registers MassTransit once with every module's consumers (Messaging:Transport - InMemory | PostgreSql |
// RabbitMq) and the enum lookup with every module's enums.
builder.AddModules();

// Register Hangfire dashboard filter
builder.Services.AddSingleton<HangfireDashboardAuthorizationFilter>();

var app = builder.Build();

// Must be first: resolves the real client IP/scheme from X-Forwarded-* sent by TRUSTED proxies only
// (ForwardedHeaders:KnownProxies / KnownNetworks / TrustPrivateNetworks).
app.UseTrustedForwardedHeaders(configuration);

app.UseConfigurableCors();

// Error handling must be OUTER to auth so 401/403 responses get JSON bodies via UseStatusCodePages
app.UseErrorHandling();

// Authentication → tenant resolution → authorization (tenant permissions need the resolved membership).
app.UseIdentityPipeline(afterAuthentication: a => a.UseMultiTenancy());

// Rate limiting runs AFTER authentication so policies can partition per user id (anonymous → per client IP).
app.UseRateLimiter();

var hangfireDashboardPath = configuration.GetValue<string>("Hangfire:DashboardPath") ?? "/hangfire";
app.UseHangfireDashboard(
    hangfireDashboardPath,
    new DashboardOptions
    {
        Authorization = [app.Services.GetRequiredService<HangfireDashboardAuthorizationFilter>()]
    });

// Request timeouts AFTER the Hangfire dashboard (it short-circuits above, so it is exempt) and before the endpoints.
app.UseRequestTimeouts();

// Module init in ModuleOrder: Tenancy first among the data modules (the Default tenant must exist before tenant-data
// seeding), Notification last (maps the realtime hub).
app.UseModules();

if (migrateOnly)
{
    // The module Use* calls above already migrated + seeded the shared database(s) (Identity, Tenancy, ...).
    // Now the dedicated tenant databases; a failed tenant fails the run so the deploy stops before the new API starts.
    // The PostgreSql message-transport schema (DDL + CREATE ROLE) is created here too: its hosted service never runs in
    // this mode, and app instances with Database:MigrateOnStartup=false must not do it.
    await app.Services.MigrateMessagingTransportAsync(CancellationToken.None);

    var failedTenants = await app.Services.GetRequiredService<DedicatedTenantDatabaseMigrator>()
        .MigrateAllAsync(CancellationToken.None);
    Console.WriteLine(failedTenants == 0
        ? "Migrate-only run completed."
        : $"Migrate-only run FAILED: {failedTenants} dedicated tenant database(s) could not be migrated.");
    return failedTenants == 0 ? 0 : 1;
}

// Transactional outbox safety net (delivers what immediate post-commit delivery missed; purges delivered rows).
app.UseOutboxSweep();

app.UseSharedInfrastructure();

await app.RunAsync();
return 0;

public partial class Program { }
