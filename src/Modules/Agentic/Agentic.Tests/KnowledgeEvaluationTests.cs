using Agentic.Options;
using Agentic.Runtime.Knowledge;
using Agentic.Runtime.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace Agentic.Tests;

/// <summary>
/// Embedding evaluations: does a REAL embedding model put a question close to the passage that answers it, when they
/// share no words, in English and Arabic? Like <see cref="ModelEvaluationTests"/>, they measure the model, so each is
/// SKIPPED unless a provider is configured through the environment:
/// <code>
/// AGENTIC_EVAL_EMBEDDING_MODEL=&lt;embedding model id&gt;   (e.g. text-embedding-3-small)
/// AGENTIC_EVAL_EMBEDDING_DIMENSIONS=&lt;n&gt;               (optional)
/// AGENTIC_EVAL_API_KEY=&lt;key&gt;                          (never committed)
/// AGENTIC_EVAL_ENDPOINT=&lt;url&gt;                         (optional)
/// dotnet test src/Modules/Agentic/Agentic.Tests --filter "Category=ModelEvaluation"
/// </code>
/// They go through <see cref="KnowledgeEmbedder"/>, the path indexing and search use, and cost a fraction of a cent.
/// </summary>
[Trait("Category", "ModelEvaluation")]
public class KnowledgeEvaluationTests
{
    private static readonly (string Key, string Text)[] Corpus =
    [
        ("customs", "Goods leaving the free zone must be declared to customs with a commercial invoice and a packing list."),
        ("storage", "Containers kept at the port longer than five days are charged a daily storage fee."),
        ("refund", "A customer may return an unused item within thirty days and receive their money back."),
        ("customs-ar", "يجب التصريح عن البضائع الخارجة من المنطقة الحرة لدى الجمارك مع فاتورة تجارية وقائمة تعبئة."),
        ("leave", "Employees accrue two days of paid holiday for every month worked.")
    ];

    private static KnowledgeEmbedder? ConfiguredEmbedder()
    {
        var model = Environment.GetEnvironmentVariable("AGENTIC_EVAL_EMBEDDING_MODEL");
        var key = Environment.GetEnvironmentVariable("AGENTIC_EVAL_API_KEY");
        if (string.IsNullOrWhiteSpace(model) || string.IsNullOrWhiteSpace(key))
            return null;

        var settings = new AgenticOptions
        {
            Enabled = true,
            Providers =
            {
                ["eval"] = new AgentProviderOptions
                {
                    Kind = AgentProviderKind.OpenAiCompatible,
                    ApiKey = key,
                    Endpoint = Environment.GetEnvironmentVariable("AGENTIC_EVAL_ENDPOINT") is { Length: > 0 } endpoint ? endpoint : null
                }
            },
            Knowledge =
            {
                Enabled = true,
                EmbeddingProvider = "eval",
                EmbeddingModelId = model,
                Dimensions = int.TryParse(Environment.GetEnvironmentVariable("AGENTIC_EVAL_EMBEDDING_DIMENSIONS"), out var d) ? d : null
            }
        };
        var options = Microsoft.Extensions.Options.Options.Create(settings);
        return new KnowledgeEmbedder(options, new AgentChatClientFactory(options, [], NullLoggerFactory.Instance));
    }

    private static double Cosine(float[] a, float[] b)
    {
        double dot = 0, na = 0, nb = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            na += a[i] * a[i];
            nb += b[i] * b[i];
        }

        return dot / (Math.Sqrt(na) * Math.Sqrt(nb));
    }

    [Theory]
    [InlineData("What paperwork do I need to take merchandise out of the duty-free area?", "customs")]
    [InlineData("How much does it cost if my box sits at the harbour for a week?", "storage")]
    [InlineData("Can I get reimbursed for something I bought last week?", "refund")]
    [InlineData("ما المستندات المطلوبة لإخراج السلع من المنطقة المعفاة من الرسوم؟", "customs")]
    public async Task AParaphrasedQuestion_IsClosestToThePassageThatAnswersIt(string question, string expected)
    {
        var embedder = ConfiguredEmbedder();
        Assert.SkipUnless(embedder is not null,
            "No embedding model configured (AGENTIC_EVAL_EMBEDDING_MODEL, AGENTIC_EVAL_API_KEY).");

        var vectors = await embedder!.EmbedAsync([question, .. Corpus.Select(c => c.Text)], TestContext.Current.CancellationToken);

        var ranked = Corpus.Select((c, i) => (c.Key, Score: Cosine(vectors[0], vectors[i + 1])))
            .OrderByDescending(x => x.Score).Select(x => x.Key).ToList();
        ranked[0].Should().StartWith(expected, $"the ranking was {string.Join(", ", ranked)}");
        if (expected == "customs")
            ranked.Take(2).Should().Contain(["customs", "customs-ar"], "the same rule in either language is close");
    }

    [Fact]
    public async Task Vectors_HaveTheConfiguredLength_SoTheHnswIndexCanCoverThem()
    {
        var embedder = ConfiguredEmbedder();
        Assert.SkipUnless(embedder is not null,
            "No embedding model configured (AGENTIC_EVAL_EMBEDDING_MODEL, AGENTIC_EVAL_API_KEY).");

        var vectors = await embedder!.EmbedAsync(["one", "two"], TestContext.Current.CancellationToken);

        vectors.Select(v => v.Length).Distinct().Should().ContainSingle()
            .Which.Should().BeLessThanOrEqualTo(KnowledgeVectorIndex.MaxHalfVectorDimensions);
        if (int.TryParse(Environment.GetEnvironmentVariable("AGENTIC_EVAL_EMBEDDING_DIMENSIONS"), out var configured))
            vectors[0].Length.Should().Be(configured);
    }
}
