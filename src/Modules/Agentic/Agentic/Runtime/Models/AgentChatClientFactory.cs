using System.ClientModel;
using System.ClientModel.Primitives;
using System.Collections.Concurrent;
using Agentic.Contracts.Scripting;
using Agentic.Runtime.Knowledge;
using Agentic.Telemetry;
using Anthropic;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OpenAI;

namespace Agentic.Runtime.Models;

/// <summary>
/// Builds (once per model key) the <see cref="IChatClient"/> of a configured model: the provider's own client wrapped
/// with the OpenTelemetry chat middleware (gen_ai spans on the module's activity source; prompt and tool content only
/// when <c>Agentic:Telemetry:CaptureContent</c> is on).
/// <para>
/// <b>HTTP.</b> The provider clients share one <see cref="HttpClient"/> owned by this factory, NOT an
/// <c>IHttpClientFactory</c> client: the host adds a resilience handler to every factory client whose per-attempt
/// timeout (30 s) and retry count suit small API calls, not model requests that legitimately run for minutes. Each
/// provider client applies its own timeout and bounded retries from <see cref="AgentProviderOptions"/>.
/// </para>
/// <para>
/// <b>Endpoints</b> are operator configuration, never tenant input. If a product ever lets a tenant supply a model
/// URL, that client must connect through <c>Shared.Core.Net.GuardedConnector</c> like webhooks do.
/// </para>
/// </summary>
public sealed class AgentChatClientFactory : IDisposable
{
    private readonly IOptions<AgenticOptions> _options;
    private readonly IEnumerable<IAgentScript> _scripts;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ConcurrentDictionary<string, Lazy<IChatClient>> _clients = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lazy<HttpClient> _http = new(CreateHttpClient);
    private readonly Lazy<IEmbeddingGenerator<string, Embedding<float>>> _embeddings;

    public AgentChatClientFactory(IOptions<AgenticOptions> options, IEnumerable<IAgentScript> scripts,
        ILoggerFactory loggerFactory)
    {
        _options = options;
        _scripts = scripts;
        _loggerFactory = loggerFactory;
        _embeddings = new Lazy<IEmbeddingGenerator<string, Embedding<float>>>(CreateEmbeddingGenerator);
    }

    /// <summary>The embedding generator of knowledge (<c>Agentic:Knowledge</c>), created once.</summary>
    public IEmbeddingGenerator<string, Embedding<float>> GetEmbeddingGenerator() => _embeddings.Value;

    /// <summary>Vector length of the deterministic embeddings of the scripted provider when none is configured.</summary>
    public const int DefaultHashingDimensions = 256;

    private IEmbeddingGenerator<string, Embedding<float>> CreateEmbeddingGenerator()
    {
        var settings = _options.Value;
        var knowledge = settings.Knowledge;
        if (!settings.Providers.TryGetValue(knowledge.EmbeddingProvider, out var provider))
            throw new InvalidOperationException($"Agentic:Providers has no provider '{knowledge.EmbeddingProvider}' (knowledge embeddings).");

        var inner = provider.Kind switch
        {
            AgentProviderKind.Scripted =>
                (IEmbeddingGenerator<string, Embedding<float>>)new HashingEmbeddingGenerator(knowledge.Dimensions ?? DefaultHashingDimensions),
            AgentProviderKind.OpenAiCompatible => new OpenAI.Embeddings.EmbeddingClient(knowledge.EmbeddingModelId,
                    Credential(provider), ClientOptions(provider))
                .AsIEmbeddingGenerator(knowledge.Dimensions),
            _ => throw new InvalidOperationException($"The provider kind '{provider.Kind}' has no embeddings API.")
        };

        var captureContent = settings.Telemetry.CaptureContent;
        return new EmbeddingGeneratorBuilder<string, Embedding<float>>(inner)
            .UseOpenTelemetry(_loggerFactory, AgenticTelemetry.SourceName, generator => generator.EnableSensitiveData = captureContent)
            .Build();
    }

    public IChatClient Get(string modelKey) =>
        _clients.GetOrAdd(modelKey, key => new Lazy<IChatClient>(() => Create(key))).Value;

    /// <summary>Tests: answer for <paramref name="modelKey"/> with <paramref name="client"/> (a provider's behaviour).</summary>
    internal void Use(string modelKey, IChatClient client) => _clients[modelKey] = new Lazy<IChatClient>(client);

    private IChatClient Create(string modelKey)
    {
        var settings = _options.Value;
        if (!settings.Models.TryGetValue(modelKey, out var model))
            throw new InvalidOperationException($"Agentic:Models has no model '{modelKey}'.");
        if (!settings.Providers.TryGetValue(model.Provider, out var provider))
            throw new InvalidOperationException($"Agentic:Providers has no provider '{model.Provider}' (model '{modelKey}').");

        var inner = provider.Kind switch
        {
            AgentProviderKind.Scripted => new ScriptedChatClient(_scripts),
            AgentProviderKind.OpenAiCompatible => CreateOpenAiCompatible(model, provider),
            AgentProviderKind.Anthropic => CreateAnthropic(model, provider),
            _ => throw new InvalidOperationException($"Unknown provider kind '{provider.Kind}'.")
        };

        var captureContent = settings.Telemetry.CaptureContent;
        return new ChatClientBuilder(inner)
            .UseOpenTelemetry(_loggerFactory, AgenticTelemetry.SourceName, client => client.EnableSensitiveData = captureContent)
            .Build();
    }

    private IChatClient CreateOpenAiCompatible(AgentModelOptions model, AgentProviderOptions provider) =>
        new OpenAI.Chat.ChatClient(model.ModelId, Credential(provider), ClientOptions(provider)).AsIChatClient();

    private OpenAIClientOptions ClientOptions(AgentProviderOptions provider)
    {
        var clientOptions = new OpenAIClientOptions
        {
            NetworkTimeout = TimeSpan.FromSeconds(provider.TimeoutSeconds),
            RetryPolicy = new ClientRetryPolicy(provider.MaxRetries),
            Transport = new HttpClientPipelineTransport(_http.Value)
        };
        if (!string.IsNullOrWhiteSpace(provider.Endpoint))
            clientOptions.Endpoint = new Uri(provider.Endpoint);
        return clientOptions;
    }

    // A self-hosted server may need no key, but the client requires a non-empty credential.
    private static ApiKeyCredential Credential(AgentProviderOptions provider) =>
        new(string.IsNullOrWhiteSpace(provider.ApiKey) ? "unused" : provider.ApiKey);

    private IChatClient CreateAnthropic(AgentModelOptions model, AgentProviderOptions provider)
    {
        var client = new AnthropicClient
        {
            ApiKey = provider.ApiKey,
            HttpClient = _http.Value,
            MaxRetries = provider.MaxRetries,
            Timeout = TimeSpan.FromSeconds(provider.TimeoutSeconds)
        };
        if (!string.IsNullOrWhiteSpace(provider.Endpoint))
            client = (AnthropicClient)client.WithOptions(o => o with { BaseUrl = provider.Endpoint });

        return client.AsIChatClient(model.ModelId, model.MaxOutputTokens);
    }

    private static HttpClient CreateHttpClient() =>
        new(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            AllowAutoRedirect = false,
            UseCookies = false,
            ConnectTimeout = TimeSpan.FromSeconds(15)
        }, disposeHandler: true)
        {
            // Each provider client enforces its own per-request timeout.
            Timeout = Timeout.InfiniteTimeSpan
        };

    public void Dispose()
    {
        if (_http.IsValueCreated)
            _http.Value.Dispose();
    }
}
