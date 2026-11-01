using Api.Extensions;
using Api.Infrastructure;
using Identity;
using Attachment;
using Subscriptions;
using Catalog;
using Shared.Messaging.Extensions;
using Shared.Core.Extensions;
using Shared.Email;
using Shared.Notification;
using Aspire.Seq;
using Hangfire;
using Hangfire.Dashboard;
using Attachment.Contracts.Attachments.Dtos;

var builder = WebApplication.CreateBuilder(args);

// Add Docker Secrets support (reads from /run/secrets/ if available)
builder.Configuration.AddDockerSecrets();

var configuration = builder.Configuration;

builder.AddSharedInfrastructure();

// Add Hangfire
builder.Services.AddHangfireServices(configuration);

// Add Rate Limiting — all policies configured via RateLimiting:Policies[] in appsettings
builder.Services.AddConfigurableRateLimiting(configuration);

builder.AddSeqEndpoint("seq");

// Register MassTransit once with all module assemblies
builder.Services.AddMassTransitWithAssemblies(
    IdentityRoot.AssemblyName,
    AttachmentRoot.AssemblyName,
    SubscriptionsRoot.AssemblyName,
    CatalogRoot.AssemblyName);

// Register Generic Enum Lookup Service (using Contracts assemblies where enums are defined)
builder.Services.AddEnumLookup(
    typeof(AttachmentType).Assembly, // Attachment.Contracts assembly
    IdentityRoot.AssemblyName, // Identity assembly (for SmsProvider, etc.)
    SubscriptionsRoot.AssemblyName, // Subscriptions assembly
    NotificationRoot.AssemblyName);

builder.Services.AddIdentityModule(configuration);
builder.Services.AddAttachmentModule(configuration);
builder.Services.AddCatalogModule(configuration);
builder.Services.AddSubscriptionsModule(configuration);
builder.Services.AddEmailInfrastructure(configuration);
builder.Services.AddNotificationInfrastructure(configuration);

// Register Hangfire dashboard filter
builder.Services.AddSingleton<HangfireDashboardAuthorizationFilter>();

var app = builder.Build();

app.UseConfigurableCors();

// Use Rate Limiting
app.UseRateLimiter();

// Error handling must be OUTER to auth so 401/403 responses get JSON bodies via UseStatusCodePages
app.UseErrorHandling();

app.UseIdentityModule();

var hangfireDashboardPath = configuration.GetValue<string>("Hangfire:DashboardPath") ?? "/hangfire";
app.UseHangfireDashboard(
    hangfireDashboardPath,
    new DashboardOptions
    {
        Authorization = [app.Services.GetRequiredService<HangfireDashboardAuthorizationFilter>()]
    });

app.UseAttachmentModule();
app.UseCatalogModule();
app.UseSubscriptionsModule();
app.UseEmailInfrastructure();
app.UseNotificationInfrastructure();

app.UseSharedInfrastructure();

await app.RunAsync();

public partial class Program { }
