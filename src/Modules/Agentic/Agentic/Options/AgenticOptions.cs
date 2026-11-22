using Agentic.Runtime.Knowledge;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Agentic.Options;

/// <summary>Bound from the <c>"Agentic"</c> appsettings section. See wiki/Modules/Agentic.md.</summary>
public sealed class AgenticOptions
{
    public const string SectionName = "Agentic";

    /// <summary>Hangfire queue of the agent runs (served by the <c>Hangfire:Servers:Agentic</c> server).</summary>
    public const string Queue = "agentic";

    /// <summary>
    /// Off by default: the endpoints answer 404 and nothing is executed, so a product that does not use agents needs no
    /// model provider. Turn it on together with at least one model.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>The model (a key of <see cref="Models"/>) of agents that name none.</summary>
    public string DefaultModel { get; set; } = "default";

    /// <summary>Models by key. Agents and tenant policies refer to the KEY, never to a provider's model id.</summary>
    public Dictionary<string, AgentModelOptions> Models { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Provider connections by name. Credentials come from configuration or Docker secrets, never the database.</summary>
    public Dictionary<string, AgentProviderOptions> Providers { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public AgentRunLimitOptions Limits { get; set; } = new();

    /// <summary>
    /// A running run renews its lease at every checkpoint; another worker may take over once it expires. Before a
    /// model request or a tool call that may take longer (provider timeout x retries, the tool's timeout), the lease is
    /// extended to cover it, so this is also how long a crashed worker's run waits before it is picked up again.
    /// </summary>
    public int LeaseSeconds { get; set; } = 300;

    /// <summary>Consecutive failed attempts (no step saved in between) before a run is marked <c>Failed</c>.</summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>
    /// A run without a live lease that was not queued for this many minutes (a lost enqueue, a crashed worker) is
    /// queued again by <c>RequeueStalledAgentRunsJob</c>.
    /// </summary>
    public int StalledAfterMinutes { get; set; } = 10;

    public string SweepCron { get; set; } = "*/5 * * * *";

    /// <summary>Live progress (hub event <c>agentic.run-progress</c>): at most one per run per this interval, plus every change of status.</summary>
    public int ProgressIntervalMilliseconds { get; set; } = 1_000;

    public AgentApprovalOptions Approvals { get; set; } = new();

    public AgentTriggerOptions Triggers { get; set; } = new();

    public AgentTelemetryOptions Telemetry { get; set; } = new();

    public KnowledgeOptions Knowledge { get; set; } = new();

    public AgentMemoryOptions Memory { get; set; } = new();

    public AgentContentOptions Content { get; set; } = new();

    public AgentConversationOptions Conversations { get; set; } = new();

    public AgentStreamingOptions Streaming { get; set; } = new();
}

/// <summary>Retrieval over documents the modules index (ADR-0028). Off by default; needs an embedding model.</summary>
public sealed class KnowledgeOptions
{
    public bool Enabled { get; set; }

    /// <summary>A key of <see cref="AgenticOptions.Providers"/> (OpenAI-compatible, or Scripted for tests). Anthropic has no embeddings API.</summary>
    public string EmbeddingProvider { get; set; } = string.Empty;

    /// <summary>The provider's embedding model id, e.g. <c>text-embedding-3-small</c>.</summary>
    public string EmbeddingModelId { get; set; } = string.Empty;

    /// <summary>Vector length to request (models that support shortening); null = the model's default.</summary>
    public int? Dimensions { get; set; }

    public int EmbeddingBatchSize { get; set; } = 64;

    /// <summary>Largest file read for indexing.</summary>
    public long MaxDocumentBytes { get; set; } = 10 * 1024 * 1024;

    public int MaxPdfPages { get; set; } = 500;

    /// <summary>Largest extracted text, in characters (also the limit of text given directly).</summary>
    public int MaxTextLength { get; set; } = 1_000_000;

    /// <summary>Passage length in characters, and the overlap between consecutive passages.</summary>
    public int ChunkSize { get; set; } = 1_200;

    public int ChunkOverlap { get; set; } = 150;

    /// <summary>Attempts of one revision (a provider outage) before the document is marked <c>Failed</c>.</summary>
    public int MaxAttempts { get; set; } = 4;

    /// <summary>Candidates taken from each of the keyword and the vector search before they are merged.</summary>
    public int SearchCandidates { get; set; } = 40;

    /// <summary>
    /// <c>None</c>: exact vector search (the default). <c>Hnsw</c>: an approximate HNSW index for the configured
    /// <see cref="Dimensions"/>, built at deploy time (ADR-0032); requires <see cref="Dimensions"/> (at most 4000).
    /// </summary>
    public KnowledgeVectorIndexKind VectorIndex { get; set; } = KnowledgeVectorIndexKind.None;

    /// <summary>HNSW: links per node (<c>m</c>); more is better recall, a larger index and a slower build.</summary>
    public int HnswM { get; set; } = 16;

    /// <summary>HNSW: candidates while building (<c>ef_construction</c>), at least twice <see cref="HnswM"/>.</summary>
    public int HnswEfConstruction { get; set; } = 64;

    /// <summary>HNSW: candidates per query (<c>hnsw.ef_search</c>), at least <see cref="SearchCandidates"/>.</summary>
    public int HnswEfSearch { get; set; } = 100;

    /// <summary>Most passages one search returns.</summary>
    public int MaxResults { get; set; } = 8;

    /// <summary>A document still pending after this long without being queued is queued again.</summary>
    public int StalledAfterMinutes { get; set; } = 15;
}

public sealed class AgentMemoryOptions
{
    /// <summary>A memory expires this many days after it was written. 0 = never.</summary>
    public int RetentionDays { get; set; } = 365;

    public int MaxPerUser { get; set; } = 100;

    public int MaxPerTenant { get; set; } = 500;

    /// <summary>Most memories <c>memory.recall</c> returns (newest first).</summary>
    public int MaxRecalled { get; set; } = 50;
}

public sealed class AgentContentOptions
{
    /// <summary>
    /// Prompts, inputs, outputs, transcript text, tool arguments and results of FINISHED runs are erased this many days
    /// after the run finished (counters, statuses and audit entries stay). 0 = keep them.
    /// </summary>
    public int RetentionDays { get; set; } = 90;
}

/// <summary>The answer's text sent to the initiator while the model writes it (<c>agentic.run-text</c>).</summary>
public sealed class AgentStreamingOptions
{
    /// <summary>Stream answers of runs started by a person or an API key (models opt out with <c>Streaming=false</c>).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Deltas are collected and sent at most once per interval (and once at the end of the answer).</summary>
    public int IntervalMilliseconds { get; set; } = 100;
}

public sealed class AgentConversationOptions
{
    /// <summary>Earlier turns a conversation turn is given at most (the newest ones; each is a message and its answer).</summary>
    public int MaxHistoryTurns { get; set; } = 20;

    /// <summary>
    /// Characters of earlier turns a conversation turn is given at most; older turns are dropped first. Keeps the cost of
    /// a long conversation bounded (history counts against the run's <c>MaxTokens</c>).
    /// </summary>
    public int MaxHistoryCharacters { get; set; } = 24_000;

    /// <summary>
    /// Messages a conversation may hold (at least 1); then 409 <c>conversation_full</c> (start a new one). It also bounds
    /// what <c>GET /agent-conversations/{id}</c> returns.
    /// </summary>
    public int MaxMessages { get; set; } = 200;

    /// <summary>
    /// A summary is written once more than this many finished turns are not covered by it (ADR-0031); 0 = never
    /// summarize (older turns are simply dropped from the history).
    /// </summary>
    public int SummarizeAfterTurns { get; set; } = 16;

    /// <summary>The newest turns a summary always leaves out: they stay in the history word for word.</summary>
    public int KeepRecentTurns { get; set; } = 8;

    /// <summary>Model key that writes summaries (a key of <c>Agentic:Models</c>); empty = <c>DefaultModel</c>.</summary>
    public string? SummaryModel { get; set; }

    /// <summary>A longer summary is cut to this length.</summary>
    public int MaxSummaryCharacters { get; set; } = 4_000;
}

public sealed class AgentTriggerOptions
{
    /// <summary>
    /// Runs one agent may be started by events and schedules per tenant and hour. Bounds a loop of events that keep
    /// triggering each other. 0 = no limit.
    /// </summary>
    public int MaxAutomaticRunsPerHour { get; set; } = 60;
}

public sealed class AgentApprovalOptions
{
    /// <summary>A request nobody decided within this time expires, and its run fails with <c>approval_expired</c>.</summary>
    public int ExpireAfterHours { get; set; } = 72;

    /// <summary>
    /// Four-eyes rule: the user a run acts for may not approve that run's own actions. Off by default, because the
    /// usual flow is an employee reviewing what their own agent prepared.
    /// </summary>
    public bool RequireDifferentApprover { get; set; }

    public string ExpiryCron { get; set; } = "*/10 * * * *";
}

public sealed class AgentModelOptions
{
    /// <summary>A key of <see cref="AgenticOptions.Providers"/>.</summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>The provider's model id (deployment name for Azure OpenAI).</summary>
    public string ModelId { get; set; } = string.Empty;

    public int MaxOutputTokens { get; set; } = 4096;

    /// <summary>Price per million input tokens, in the currency you report costs in. 0 = cost not estimated.</summary>
    public decimal InputPricePerMillionTokens { get; set; }

    public decimal OutputPricePerMillionTokens { get; set; }

    /// <summary>Stream this model's answers when <c>Agentic:Streaming:Enabled</c> (false for a server that cannot).</summary>
    public bool Streaming { get; set; } = true;
}

public enum AgentProviderKind
{
    /// <summary>Deterministic scripts (<c>IAgentScript</c>): tests, CI, demos. Refused in Production.</summary>
    Scripted = 0,

    /// <summary>OpenAI, Azure OpenAI (v1 endpoint) and any server that speaks the OpenAI chat API (Ollama, vLLM, ...).</summary>
    OpenAiCompatible = 1,

    Anthropic = 2
}

public sealed class AgentProviderOptions
{
    public AgentProviderKind Kind { get; set; } = AgentProviderKind.Scripted;

    /// <summary>Base URL. Optional for OpenAI and Anthropic; required for a self-hosted OpenAI-compatible server.</summary>
    public string? Endpoint { get; set; }

    /// <summary>API key (Docker secret or environment variable). A self-hosted server may need none.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Time allowed for one model request.</summary>
    public int TimeoutSeconds { get; set; } = 120;

    /// <summary>Retries of a failed model request inside the provider client (429, 5xx, timeouts), with backoff.</summary>
    public int MaxRetries { get; set; } = 2;
}

/// <summary>
/// Ceilings of ONE run. An agent definition may lower them (<c>AgentLimits</c>), never raise them. They bound cost and
/// stop a run that loops.
/// </summary>
public sealed class AgentRunLimitOptions
{
    public int MaxModelTurns { get; set; } = 12;

    public int MaxToolCalls { get; set; } = 30;

    /// <summary>Tool calls taken from one model turn; further calls in the same turn are ignored.</summary>
    public int MaxToolCallsPerTurn { get; set; } = 8;

    /// <summary>Input + output tokens of the whole run.</summary>
    public long MaxTokens { get; set; } = 200_000;

    /// <summary>Active processing time (waiting for an approval does not count).</summary>
    public int MaxDurationSeconds { get; set; } = 600;

    /// <summary>
    /// Runs of ONE tenant that may be queued, running or waiting at the same time (a plan may lower it with the limit
    /// <c>max_concurrent_agent_runs</c>). 0 = no limit.
    /// </summary>
    public int MaxConcurrentRunsPerTenant { get; set; } = 20;

    public int MaxPromptLength { get; set; } = 8_000;

    public int MaxInputBytes { get; set; } = 32 * 1024;

    /// <summary>
    /// Arguments of one tool call (the typed result included). Larger arguments are refused when the model's answer
    /// is saved, and never stored.
    /// </summary>
    public int MaxToolArgumentsBytes { get; set; } = 32 * 1024;

    /// <summary>
    /// Text of one model answer. A larger answer is not stored and stops the run with <c>output_too_large</c>, whatever
    /// the provider's own output limit is (an OpenAI-compatible server may ignore it).
    /// </summary>
    public int MaxModelTextBytes { get; set; } = 64 * 1024;

    /// <summary>A larger tool result is replaced by a <c>result_too_large</c> error the model can act on.</summary>
    public int MaxToolResultBytes { get; set; } = 64 * 1024;
}

public sealed class AgentTelemetryOptions
{
    /// <summary>
    /// Adds prompts, tool arguments and results to model spans. Off by default: that content is business data and may
    /// be confidential. Turn it on only in an environment whose trace store may hold it.
    /// </summary>
    public bool CaptureContent { get; set; }
}

/// <summary>Fails startup on a configuration that could not run (only when the module is enabled).</summary>
public sealed class AgenticOptionsValidator : IValidateOptions<AgenticOptions>
{
    public ValidateOptionsResult Validate(string? name, AgenticOptions options)
    {
        if (!options.Enabled)
            return ValidateOptionsResult.Success;

        var errors = new List<string>();
        if (!options.Models.ContainsKey(options.DefaultModel))
            errors.Add($"Agentic:DefaultModel '{options.DefaultModel}' is not one of Agentic:Models.");

        foreach (var (key, model) in options.Models)
        {
            if (string.IsNullOrWhiteSpace(model.ModelId))
                errors.Add($"Agentic:Models:{key}:ModelId is required.");
            if (model.MaxOutputTokens < 1)
                errors.Add($"Agentic:Models:{key}:MaxOutputTokens must be at least 1.");
            if (model.InputPricePerMillionTokens < 0 || model.OutputPricePerMillionTokens < 0)
                errors.Add($"Agentic:Models:{key}: prices must not be negative.");
            if (!options.Providers.TryGetValue(model.Provider, out var provider))
            {
                errors.Add($"Agentic:Models:{key}:Provider '{model.Provider}' is not one of Agentic:Providers.");
                continue;
            }

            ValidateProvider(model.Provider, provider, errors);
        }

        var limits = options.Limits;
        if (limits.MaxModelTurns < 1 || limits.MaxToolCalls < 0 || limits.MaxToolCallsPerTurn < 1 || limits.MaxTokens < 1 ||
            limits.MaxDurationSeconds < 1 || limits.MaxPromptLength < 1 || limits.MaxInputBytes < 1 ||
            limits.MaxToolArgumentsBytes < 1 || limits.MaxToolResultBytes < 1 || limits.MaxModelTextBytes < 1)
        {
            errors.Add("Agentic:Limits: every limit must be positive.");
        }

        if (options.LeaseSeconds < 30)
            errors.Add("Agentic:LeaseSeconds must be at least 30.");
        if (options.MaxAttempts < 1)
            errors.Add("Agentic:MaxAttempts must be at least 1.");
        if (options.StalledAfterMinutes < 1)
            errors.Add("Agentic:StalledAfterMinutes must be at least 1.");
        if (string.IsNullOrWhiteSpace(options.SweepCron))
            errors.Add("Agentic:SweepCron is required.");
        if (limits.MaxConcurrentRunsPerTenant < 0 || options.Triggers.MaxAutomaticRunsPerHour < 0)
            errors.Add("Agentic: MaxConcurrentRunsPerTenant and MaxAutomaticRunsPerHour must not be negative.");
        if (options.Approvals.ExpireAfterHours < 1)
            errors.Add("Agentic:Approvals:ExpireAfterHours must be at least 1.");
        if (string.IsNullOrWhiteSpace(options.Approvals.ExpiryCron))
            errors.Add("Agentic:Approvals:ExpiryCron is required.");
        if (options.Content.RetentionDays < 0 || options.Memory.RetentionDays < 0)
            errors.Add("Agentic: Content:RetentionDays and Memory:RetentionDays must not be negative.");
        if (options.Memory.MaxPerUser < 1 || options.Memory.MaxPerTenant < 1 || options.Memory.MaxRecalled < 1)
            errors.Add("Agentic:Memory: MaxPerUser, MaxPerTenant and MaxRecalled must be positive.");
        if (options.Conversations.MaxHistoryTurns < 0 || options.Conversations.MaxHistoryCharacters < 0)
            errors.Add("Agentic:Conversations: MaxHistoryTurns and MaxHistoryCharacters must not be negative.");
        var conversations = options.Conversations;
        if (conversations.SummarizeAfterTurns < 0)
            errors.Add("Agentic:Conversations:SummarizeAfterTurns must not be negative.");
        if (conversations.SummarizeAfterTurns > 0)
        {
            if (conversations.KeepRecentTurns < 1 || conversations.KeepRecentTurns >= conversations.SummarizeAfterTurns)
                errors.Add("Agentic:Conversations:KeepRecentTurns must be at least 1 and less than SummarizeAfterTurns.");
            if (conversations.MaxSummaryCharacters is < 200 or > AgentConversation.SummaryMaxLength)
                errors.Add($"Agentic:Conversations:MaxSummaryCharacters must be between 200 and {AgentConversation.SummaryMaxLength}.");
            if (!string.IsNullOrWhiteSpace(conversations.SummaryModel) && !options.Models.ContainsKey(conversations.SummaryModel))
                errors.Add($"Agentic:Conversations:SummaryModel '{conversations.SummaryModel}' is not one of Agentic:Models.");
        }

        if (options.Streaming.IntervalMilliseconds < 0)
            errors.Add("Agentic:Streaming:IntervalMilliseconds must not be negative.");
        if (options.Conversations.MaxMessages < 1)
            errors.Add("Agentic:Conversations:MaxMessages must be at least 1.");

        ValidateKnowledge(options, errors);

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }

    private static void ValidateKnowledge(AgenticOptions options, List<string> errors)
    {
        var knowledge = options.Knowledge;
        if (!knowledge.Enabled)
            return;

        if (!options.Providers.TryGetValue(knowledge.EmbeddingProvider, out var provider))
            errors.Add($"Agentic:Knowledge:EmbeddingProvider '{knowledge.EmbeddingProvider}' is not one of Agentic:Providers.");
        else if (provider.Kind == AgentProviderKind.Anthropic)
            errors.Add("Agentic:Knowledge:EmbeddingProvider: Anthropic has no embeddings API; use an OpenAI-compatible provider.");
        if (string.IsNullOrWhiteSpace(knowledge.EmbeddingModelId))
            errors.Add("Agentic:Knowledge:EmbeddingModelId is required.");
        if (knowledge.Dimensions is < 1 or > 16_000)
            errors.Add("Agentic:Knowledge:Dimensions must be between 1 and 16000.");
        if (knowledge.VectorIndex == KnowledgeVectorIndexKind.Hnsw)
        {
            if (knowledge.Dimensions is not (>= 1 and <= KnowledgeVectorIndex.MaxHalfVectorDimensions))
                errors.Add($"Agentic:Knowledge:VectorIndex=Hnsw needs Dimensions between 1 and {KnowledgeVectorIndex.MaxHalfVectorDimensions}.");
            if (knowledge.HnswM is < 2 or > 100 || knowledge.HnswEfConstruction < 2 * knowledge.HnswM ||
                knowledge.HnswEfSearch < knowledge.SearchCandidates || knowledge.HnswEfSearch > 1_000)
            {
                errors.Add("Agentic:Knowledge: HnswM must be 2-100, HnswEfConstruction at least twice HnswM, and " +
                           "HnswEfSearch between SearchCandidates and 1000.");
            }
        }
        if (knowledge.ChunkSize < 200 || knowledge.ChunkOverlap < 0 || knowledge.ChunkOverlap > knowledge.ChunkSize / 2)
            errors.Add("Agentic:Knowledge: ChunkSize must be at least 200 and ChunkOverlap at most half of it.");
        if (knowledge.EmbeddingBatchSize < 1 || knowledge.MaxDocumentBytes < 1 || knowledge.MaxPdfPages < 1 ||
            knowledge.MaxTextLength < 1 || knowledge.MaxAttempts < 1 || knowledge.SearchCandidates < 1 ||
            knowledge.MaxResults < 1 || knowledge.StalledAfterMinutes < 1)
        {
            errors.Add("Agentic:Knowledge: every limit must be positive.");
        }
    }

    private static void ValidateProvider(string name, AgentProviderOptions provider, List<string> errors)
    {
        if (provider.TimeoutSeconds < 1)
            errors.Add($"Agentic:Providers:{name}:TimeoutSeconds must be at least 1.");
        if (provider.MaxRetries < 0)
            errors.Add($"Agentic:Providers:{name}:MaxRetries must not be negative.");
        if (!string.IsNullOrWhiteSpace(provider.Endpoint) &&
            !(Uri.TryCreate(provider.Endpoint, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http"))
        {
            errors.Add($"Agentic:Providers:{name}:Endpoint must be an absolute http(s) URL.");
        }

        switch (provider.Kind)
        {
            case AgentProviderKind.Anthropic when string.IsNullOrWhiteSpace(provider.ApiKey):
                errors.Add($"Agentic:Providers:{name}:ApiKey is required for Anthropic.");
                break;
            // A hosted OpenAI-compatible API needs a key; a self-hosted server (explicit endpoint) may not.
            case AgentProviderKind.OpenAiCompatible
                when string.IsNullOrWhiteSpace(provider.ApiKey) && string.IsNullOrWhiteSpace(provider.Endpoint):
                errors.Add($"Agentic:Providers:{name}: set ApiKey, or Endpoint for a self-hosted server.");
                break;
        }
    }
}

/// <summary>
/// Refuses to start in Production while an enabled model is served by the scripted provider: its answers are canned,
/// so a deployment that forgot to configure a real provider must not look healthy. (Modules cannot add checks to the
/// host's <c>ProductionConfigurationGuard</c>, so this one lives with the module, like the malware scanner's.)
/// </summary>
public sealed class AgenticStartupCheck(IOptions<AgenticOptions> options, IHostEnvironment environment) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (!settings.Enabled || !environment.IsProduction())
            return Task.CompletedTask;

        var scripted = settings.Models
            .Where(m => settings.Providers.TryGetValue(m.Value.Provider, out var p) && p.Kind == AgentProviderKind.Scripted)
            .Select(m => m.Key)
            .ToList();
        if (settings.Knowledge.Enabled && settings.Providers.TryGetValue(settings.Knowledge.EmbeddingProvider, out var embeddings) &&
            embeddings.Kind == AgentProviderKind.Scripted)
        {
            scripted.Add("Knowledge:EmbeddingProvider");
        }

        if (scripted.Count > 0)
        {
            throw new InvalidOperationException(
                $"Agentic: the model(s) '{string.Join("', '", scripted)}' use the Scripted provider, which is for tests " +
                "and demos only. Configure a real provider (Agentic:Providers) or set Agentic:Enabled=false.");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
