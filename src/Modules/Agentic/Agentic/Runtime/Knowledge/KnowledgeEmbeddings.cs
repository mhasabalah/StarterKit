using System.Globalization;
using System.Text;
using Agentic.Runtime.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace Agentic.Runtime.Knowledge;

/// <summary>Turns passages and search queries into vectors with the configured embedding model.</summary>
public interface IKnowledgeEmbedder
{
    /// <summary>
    /// Identity of the model (<c>kind:model:dimensions</c>). Stored with every indexed document: vectors of different
    /// models are never compared, so changing the model needs a re-index (keyword search keeps working meanwhile).
    /// </summary>
    string ModelIdentity { get; }

    Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken);
}

/// <summary>
/// <see cref="IKnowledgeEmbedder"/> over the <see cref="IEmbeddingGenerator{TInput,TEmbedding}"/> built by
/// <see cref="AgentChatClientFactory"/> (OpenAI-compatible providers, or the deterministic hashing generator of the
/// scripted provider), in batches of <c>Agentic:Knowledge:EmbeddingBatchSize</c>.
/// </summary>
public sealed class KnowledgeEmbedder(IOptions<AgenticOptions> options, AgentChatClientFactory factory) : IKnowledgeEmbedder
{
    public string ModelIdentity => IdentityOf(options.Value);

    public static string IdentityOf(AgenticOptions settings)
    {
        var knowledge = settings.Knowledge;
        var kind = settings.Providers.TryGetValue(knowledge.EmbeddingProvider, out var provider)
            ? provider.Kind.ToString().ToLowerInvariant()
            : "unknown";
        var dimensions = knowledge.Dimensions?.ToString(CultureInfo.InvariantCulture) ?? "default";
        return $"{kind}:{knowledge.EmbeddingModelId}:{dimensions}";
    }

    public async Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken)
    {
        var generator = factory.GetEmbeddingGenerator();
        var batchSize = Math.Max(1, options.Value.Knowledge.EmbeddingBatchSize);
        var vectors = new List<float[]>(texts.Count);
        foreach (var batch in texts.Chunk(batchSize))
        {
            var embeddings = await generator.GenerateAsync(batch, cancellationToken: cancellationToken);
            if (embeddings.Count != batch.Length)
                throw new InvalidOperationException($"The embedding model returned {embeddings.Count} vectors for {batch.Length} inputs.");
            vectors.AddRange(embeddings.Select(e => e.Vector.ToArray()));
        }

        if (vectors.Select(v => v.Length).Distinct().Count() > 1)
            throw new InvalidOperationException("The embedding model returned vectors of different lengths.");
        return vectors;
    }
}

/// <summary>
/// Deterministic embeddings for tests, CI and demos (the scripted provider): words are hashed (FNV-1a, stable across
/// processes) into a fixed number of signed buckets and the vector is L2-normalized, so texts that share words are
/// close. It carries no meaning beyond shared words and is refused in Production with the rest of the scripted provider.
/// </summary>
public sealed class HashingEmbeddingGenerator(int dimensions) : IEmbeddingGenerator<string, Embedding<float>>
{
    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(values.Select(v => new Embedding<float>(Embed(v)))));

    public float[] Embed(string text)
    {
        var vector = new float[dimensions];
        foreach (var token in Tokens(text))
        {
            var hash = Fnv1a(token);
            var index = (int)(hash % (uint)dimensions);
            vector[index] += (hash & 0x8000_0000) == 0 ? 1f : -1f;
        }

        var norm = MathF.Sqrt(vector.Sum(x => x * x));
        if (norm > 0)
        {
            for (var i = 0; i < vector.Length; i++)
                vector[i] /= norm;
        }

        return vector;
    }

    private static IEnumerable<string> Tokens(string text)
    {
        var current = new StringBuilder();
        foreach (var c in text)
        {
            if (char.IsLetterOrDigit(c))
            {
                current.Append(char.ToLowerInvariant(c));
                continue;
            }

            if (current.Length > 0)
            {
                yield return current.ToString();
                current.Clear();
            }
        }

        if (current.Length > 0)
            yield return current.ToString();
    }

    private static uint Fnv1a(string value)
    {
        var hash = 2166136261u;
        foreach (var b in Encoding.UTF8.GetBytes(value))
        {
            hash ^= b;
            hash *= 16777619u;
        }

        return hash;
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose()
    {
    }
}
