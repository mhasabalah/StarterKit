using Agentic.Domains.AgentRuns.Jobs;
using Agentic.Domains.AgentRuns.Services;
using Agentic.Domains.Approvals.Jobs;
using Agentic.Contracts.Scripting;
using Agentic.Domains.Conversations.Jobs;
using Agentic.Domains.Conversations.Services;
using Agentic.Domains.Knowledge.Jobs;
using Agentic.Domains.Knowledge.Tools;
using Agentic.Domains.Memory.Features;
using Agentic.Domains.Memory.Tools;
using Agentic.Runtime.Execution;
using Agentic.Runtime.Knowledge;
using Agentic.Runtime.Models;
using Agentic.Runtime.Security;
using Agentic.Telemetry;
using Hangfire;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Notification.Contracts.Realtime;
using Shared.Core.Entitlements;
using Shared.Core.Messaging.Outbox;

namespace Agentic;

/// <summary>
/// AI agents (tenant-data module): durable agent runs that act only through typed, authorized tools. Business modules
/// plug in through <c>AddAgent</c> / <c>AddAgentTool</c> (Agentic.Contracts) and never reference this assembly or a
/// model SDK. The host discovers the module through <see cref="AgenticModuleManifest"/> (ADR-0019) and runs the jobs on
/// the Hangfire server of queue <see cref="AgenticOptions.Queue"/> (<c>Hangfire:Servers:Agentic</c>).
/// Inert unless <c>Agentic:Enabled=true</c>. See wiki/Modules/Agentic.md and ADR-0025.
/// </summary>
public static class AgenticModule
{
    public const string SweepJobId = "agentic-requeue-stalled";
    public const string ApprovalExpiryJobId = "agentic-expire-approvals";
    public const string AuditPurgeJobId = "agentic-purge-audit-entries";
    public const string ContentPurgeJobId = "agentic-purge-content";
    public const string KnowledgeSweepJobId = "agentic-requeue-stalled-knowledge";

    public static IServiceCollection AddAgenticModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AgenticOptions>()
            .Bind(configuration.GetSection(AgenticOptions.SectionName))
            .PostConfigure(options => OverlaySecrets(options, configuration))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<AgenticOptions>, AgenticOptionsValidator>();
        services.AddHostedService<AgenticStartupCheck>();

        services.TryAddSingleton(TimeProvider.System);
        services.AddHttpContextAccessor();
        services.AddMetrics();
        // Plan gates and quotas: permissive defaults unless a billing module registers the real ones.
        services.AddEntitlements();
        services.AddQuotaDefaults();

        services.AddScoped<IAgentRunRepository, AgentRunRepository>();
        services.AddScoped<IAgentConversationRepository, AgentConversationRepository>();
        services.AddScoped<IAgentApprovalRepository, AgentApprovalRepository>();
        services.AddScoped<IAgentPolicyRepository, AgentPolicyRepository>();
        services.AddScoped<AgentRunStarter>();
        services.AddScoped<AgentAccess>();
        services.AddScoped<Domains.Approvals.Features.AgentApprovalDecision>();
        services.AddScoped<AgentPrincipalResolver>();
        services.AddScoped<AgentCallScope>();
        services.AddScoped<IKnowledgeDocumentRepository, KnowledgeDocumentRepository>();
        services.AddScoped<IAgentMemoryRepository, AgentMemoryRepository>();
        services.AddScoped<AgentMemoryWriter>();

        // Runtime. The registry reads what every module registered (AddAgent / AddAgentTool) and is validated at startup.
        services.AddSingleton<AgentRegistry>();
        services.AddSingleton<AgentChatClientFactory>();
        services.AddSingleton<IAgentModelGateway, ChatClientModelGateway>();
        services.AddSingleton<AgenticMetrics>();
        services.AddSingleton<AgentRunProgressPublisher>();
        services.AddSingleton<AgentRunTextStreams>();
        // Live progress goes through the Notification hub; without that module (or with real time off) it is a no-op.
        services.TryAddSingleton<IUserEventPublisher, NullUserEventPublisher>();
        services.AddTransient<AgentRunExecutor>();

        // Knowledge and memory (ADR-0028): sources come from the modules (AddKnowledgeSource), validated at startup.
        services.AddSingleton<KnowledgeSourceRegistry>();
        services.AddSingleton<IKnowledgeEmbedder, KnowledgeEmbedder>();
        services.AddAgentTool<KnowledgeSearchTool>();
        services.AddAgentTool<MemoryRecallTool>();
        services.AddAgentTool<MemoryRememberTool>();

        services.AddMediator(AgenticRoot.AssemblyName);
        services.AddCarterWithAssemblies(AgenticRoot.AssemblyName);

        services.AddCustomDbContext<AgenticDbContext>(configuration, DatabaseDefaults.ConnectionStringName);
        services.AddOutbox<AgenticDbContext>(); // delivers the integration events raised by Agentic aggregates

        // Hangfire jobs
        services.AddTransient<ExecuteAgentRunJob>();
        services.AddTransient<RequeueStalledAgentRunsJob>();
        services.AddTransient<ExpireAgentApprovalsJob>();
        services.AddTransient<PurgeAgentAuditEntriesJob>();
        services.AddTransient<PurgeAgentContentJob>();
        services.AddTransient<IndexKnowledgeDocumentJob>();
        services.AddTransient<RequeueStalledKnowledgeDocumentsJob>();
        services.AddTransient<SummarizeAgentConversationJob>();
        // The scripted provider's summary writer (tests and demos; the scripted provider is refused in Production).
        services.AddAgentScript<ConversationSummaryScript>();

        return services;
    }

    public static IApplicationBuilder UseAgenticModule(this IApplicationBuilder app)
    {
        app.UseMigration<AgenticDbContext>(); // migrations (dev/tests; production migrates at deploy) + seeders

        // Fail fast on a wiring mistake in any module: duplicate names, a tool without an authorization decision, an
        // agent that lists an unknown tool or names a model that is not configured.
        var registry = app.ApplicationServices.GetRequiredService<AgentRegistry>();
        _ = app.ApplicationServices.GetRequiredService<KnowledgeSourceRegistry>();
        var options = app.ApplicationServices.GetRequiredService<IOptions<AgenticOptions>>().Value;
        if (options.Enabled)
            EnsureModelsConfigured(registry, options);

        // Safety net: runs whose Hangfire job was lost (a crash between the commit and the enqueue, a dead worker).
        using var scope = app.ApplicationServices.CreateScope();
        var recurring = scope.ServiceProvider.GetRequiredService<IRecurringJobManager>();
        recurring.AddOrUpdate<RequeueStalledAgentRunsJob>(
            SweepJobId,
            x => x.ExecuteAsync(JobCancellationToken.Null),
            options.SweepCron);
        recurring.AddOrUpdate<ExpireAgentApprovalsJob>(
            ApprovalExpiryJobId,
            x => x.ExecuteAsync(JobCancellationToken.Null),
            options.Approvals.ExpiryCron);
        recurring.AddOrUpdate<PurgeAgentAuditEntriesJob>(
            AuditPurgeJobId,
            x => x.ExecuteAsync(JobCancellationToken.Null),
            Cron.Daily(4));
        recurring.AddOrUpdate<PurgeAgentContentJob>(
            ContentPurgeJobId,
            x => x.ExecuteAsync(JobCancellationToken.Null),
            Cron.Daily(3));
        recurring.AddOrUpdate<RequeueStalledKnowledgeDocumentsJob>(
            KnowledgeSweepJobId,
            x => x.ExecuteAsync(JobCancellationToken.Null),
            options.SweepCron);

        return app;
    }

    internal static void EnsureModelsConfigured(AgentRegistry registry, AgenticOptions options)
    {
        foreach (var agent in registry.Agents)
        {
            if (agent.Definition.Model is { } model && !options.Models.ContainsKey(model))
            {
                throw new InvalidOperationException(
                    $"Agent '{agent.Definition.Key}' uses the model '{model}', which is not one of Agentic:Models.");
            }
        }
    }

    /// <summary>
    /// Provider API keys from Docker secrets: a secret file <c>agentic_&lt;provider&gt;_api_key</c> (provider name in lower
    /// case) fills <c>Agentic:Providers:&lt;provider&gt;:ApiKey</c> when the configuration left it empty.
    /// </summary>
    private static void OverlaySecrets(AgenticOptions options, IConfiguration configuration)
    {
        foreach (var (name, provider) in options.Providers)
        {
            if (!string.IsNullOrWhiteSpace(provider.ApiKey))
                continue;
            var secret = configuration[$"agentic_{name.ToLowerInvariant()}_api_key"];
            if (!string.IsNullOrWhiteSpace(secret))
                provider.ApiKey = secret.Trim();
        }
    }
}
