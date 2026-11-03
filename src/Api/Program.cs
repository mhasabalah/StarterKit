using Api.Extensions;
using Api.Infrastructure;
using Identity;
using Attachment;
using Subscriptions;
using Catalog;
using Catalog.Contracts;
using Tenancy;
using Shared.Messaging.Extensions;
using Shared.Core.Extensions;
using Shared.Core.Web;
using Notification;
using Notification.Contracts;
using Aspire.Seq;
using Hangfire;
using Hangfire.Dashboard;
using Attachment.Contracts.Attachments.Dtos;
using Shared.Core.MultiTenancy;
using Shared.Core.MultiTenancy.Web;

var builder = WebApplication.CreateBuilder(args);

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

// Register MassTransit once with all module assemblies (transport: Messaging:Transport - InMemory | PostgreSql | RabbitMq)
builder.Services.AddMassTransitWithAssemblies(
    configuration,
    IdentityRoot.AssemblyName,
    AttachmentRoot.AssemblyName,
    SubscriptionsRoot.AssemblyName,
    CatalogRoot.AssemblyName,
    TenancyRoot.AssemblyName,
    NotificationRoot.AssemblyName);

// Register Generic Enum Lookup Service (using Contracts assemblies where enums are defined)
builder.Services.AddEnumLookup(
    typeof(AttachmentType).Assembly, // Attachment.Contracts assembly
    IdentityRoot.AssemblyName, // Identity assembly (for SmsProvider, etc.)
    SubscriptionsRoot.AssemblyName, // Subscriptions assembly
    typeof(Subscriptions.Contracts.Subscriptions.Enums.SubscriptionStatus).Assembly, // Subscriptions.Contracts (SubscriptionStatus)
    CatalogContractsRoot.AssemblyName, // Catalog.Contracts (ProductStatus, ProductType, Currency, AttributeDataType)
    NotificationContractsRoot.AssemblyName, // Notification.Contracts (NotificationChannel, NotificationDeliveryStatus, EmailTemplate, ...)
    NotificationRoot.AssemblyName); // Notification (EmailProvider, delivery-failure kinds)

builder.Services.AddIdentityModule(configuration);
builder.Services.AddTenancyModule(configuration); // before Subscriptions: overrides its subscriber defaults
builder.Services.AddAttachmentModule(configuration);
builder.Services.AddCatalogModule(configuration);
builder.Services.AddSubscriptionsModule(configuration);
builder.Services.AddNotificationModule(configuration);

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
app.UseIdentityModule(afterAuthentication: a => a.UseMultiTenancy());

// Rate limiting runs AFTER authentication so policies can partition per user id (anonymous → per client IP).
app.UseRateLimiter();

var hangfireDashboardPath = configuration.GetValue<string>("Hangfire:DashboardPath") ?? "/hangfire";
app.UseHangfireDashboard(
    hangfireDashboardPath,
    new DashboardOptions
    {
        Authorization = [app.Services.GetRequiredService<HangfireDashboardAuthorizationFilter>()]
    });

app.UseTenancyModule(); // control plane first: the Default tenant must exist before tenant-data seeding
app.UseAttachmentModule();
app.UseCatalogModule();
app.UseSubscriptionsModule();
app.UseNotificationModule();

// Transactional outbox safety net (delivers what immediate post-commit delivery missed; purges delivered rows).
app.UseOutboxSweep();

app.UseSharedInfrastructure();

await app.RunAsync();

public partial class Program { }
