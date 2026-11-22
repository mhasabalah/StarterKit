using Agentic.Options;
using Agentic.Runtime.Knowledge;

namespace Agentic.Tests;

/// <summary>The HNSW index settings (ADR-0032): which index a dimension gets, and the configurations startup refuses.</summary>
public class KnowledgeVectorIndexTests
{
    private static AgenticOptions Settings(Action<KnowledgeOptions> configure)
    {
        var settings = new AgenticOptions
        {
            Enabled = true,
            Models = { ["default"] = new AgentModelOptions { Provider = "scripted", ModelId = "scripted" } },
            Providers = { ["scripted"] = new AgentProviderOptions { Kind = AgentProviderKind.Scripted } },
            Knowledge = { Enabled = true, EmbeddingProvider = "scripted", EmbeddingModelId = "hashing" }
        };
        configure(settings.Knowledge);
        return settings;
    }

    [Theory]
    [InlineData(1536, "vector(1536)", "vector_cosine_ops")]
    [InlineData(2000, "vector(2000)", "vector_cosine_ops")]
    [InlineData(3072, "halfvec(3072)", "halfvec_cosine_ops")]
    public void ADimension_GetsTheTypePgvectorCanIndex(int dimensions, string columnType, string operatorClass)
    {
        var index = new KnowledgeVectorIndex(dimensions, 100);

        index.ColumnType.Should().Be(columnType);
        index.OperatorClass.Should().Be(operatorClass);
        index.Name.Should().Be($"ix_knowledge_chunks_embedding_hnsw_{dimensions}");
    }

    [Fact]
    public void WithoutHnsw_SearchIsExact()
    {
        KnowledgeVectorIndex.From(Settings(k => k.Dimensions = 1536)).Should().BeNull();
        KnowledgeVectorIndex.From(Settings(k => { k.VectorIndex = KnowledgeVectorIndexKind.Hnsw; k.Dimensions = 1536; }))
            .Should().Be(new KnowledgeVectorIndex(1536, 100));
    }

    [Theory]
    [InlineData(null, 16, 64, 100)]
    [InlineData(4001, 16, 64, 100)]
    [InlineData(1536, 1, 64, 100)]
    [InlineData(1536, 16, 20, 100)]
    [InlineData(1536, 16, 64, 10)]
    [InlineData(1536, 16, 64, 5000)]
    public void Hnsw_WithoutADimensionOrWithBadParameters_FailsStartup(int? dimensions, int m, int efConstruction, int efSearch)
    {
        var settings = Settings(k =>
        {
            k.VectorIndex = KnowledgeVectorIndexKind.Hnsw;
            k.Dimensions = dimensions;
            k.HnswM = m;
            k.HnswEfConstruction = efConstruction;
            k.HnswEfSearch = efSearch;
        });

        new AgenticOptionsValidator().Validate(null, settings).Failed.Should().BeTrue();
    }

    [Fact]
    public void Hnsw_WithTheDefaults_AndADimension_IsValid() =>
        new AgenticOptionsValidator().Validate(null, Settings(k =>
        {
            k.VectorIndex = KnowledgeVectorIndexKind.Hnsw;
            k.Dimensions = 1536;
        })).Succeeded.Should().BeTrue();
}
