using System.Collections.Concurrent;
using System.Text;
using Agentic.Contracts.Knowledge;
using Agentic.Runtime.Knowledge;
using Attachment.Contracts.Attachments.Queries;
using Shared.Contracts.Localization;
using Shared.Contracts.Mediation;
using Shared.Core.Exceptions;
using Shared.Core.MultiTenancy;
using Shared.Core.MultiTenancy.Authorization;

namespace Agentic.IntegrationTests;

/// <summary>The owning module's side of knowledge: its sources, its document policy and its files.</summary>
public sealed class KnowledgeWorld
{
    public const string Handbook = "test.handbook";
    public const string Contracts = "test.contracts";

    /// <summary>Permission of the contracts source (Bob lacks it when a test says so).</summary>
    public const string ContractsPermission = TenantPermissions.WebhooksManage;

    public static readonly KnowledgeSourceDefinition[] Sources =
    [
        new() { Key = Handbook, Name = new LocalizedText("Handbook", "الدليل"), AllowAnyMember = true },
        new() { Key = Contracts, Name = new LocalizedText("Contracts", "العقود"), RequiredPermission = ContractsPermission }
    ];

    private readonly ConcurrentDictionary<(Guid Tenant, Guid Id), (string FileName, string ContentType, byte[] Bytes)> _files = new();

    /// <summary>Contract refs the document policy hides from everyone.</summary>
    public ConcurrentDictionary<string, bool> HiddenContracts { get; } = new(StringComparer.Ordinal);

    /// <summary>Embedding calls that fail before the provider "recovers".</summary>
    public int EmbeddingFailures { get; set; }

    public Guid AddFile(TenantInfo tenant, string fileName, string contentType, string content)
    {
        var id = Guid.NewGuid();
        _files[(tenant.Id, id)] = (fileName, contentType, Encoding.UTF8.GetBytes(content));
        return id;
    }

    public (string FileName, string ContentType, byte[] Bytes)? File(Guid? tenant, Guid id) =>
        tenant is { } t && _files.TryGetValue((t, id), out var file) ? file : null;

    public void Reset()
    {
        HiddenContracts.Clear();
        EmbeddingFailures = 0;
    }
}

/// <summary>Document-level policy of the contracts source: hides what the world says is hidden.</summary>
public sealed class ContractsPolicy(KnowledgeWorld world) : IKnowledgeSourcePolicy
{
    public string SourceKey => KnowledgeWorld.Contracts;

    public Task<IReadOnlySet<string>> GetReadableAsync(IReadOnlyCollection<string> externalRefs, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlySet<string>>(externalRefs.Where(r => !world.HiddenContracts.ContainsKey(r)).ToHashSet(StringComparer.Ordinal));
}

/// <summary>The Attachment module stand-in for the trusted content query (files are per tenant, like the real ones).</summary>
public sealed class FakeAttachmentContent(KnowledgeWorld world, ITenantContext tenant)
    : IRequestHandler<OpenAttachmentContentQuery, OpenAttachmentContentResult>
{
    public Task<OpenAttachmentContentResult> Handle(OpenAttachmentContentQuery request, CancellationToken cancellationToken)
    {
        var file = world.File(tenant.TenantId, request.AttachmentId)
                   ?? throw new NotFoundException($"Attachment {request.AttachmentId} was not found.");
        return Task.FromResult(new OpenAttachmentContentResult(new MemoryStream(file.Bytes), file.FileName, file.ContentType,
            file.Bytes.Length));
    }
}

/// <summary>The real embedder (deterministic hashing of the scripted provider) that fails while the world says so.</summary>
public sealed class FlakyEmbedder(KnowledgeEmbedder inner, KnowledgeWorld world) : IKnowledgeEmbedder
{
    public string ModelIdentity => inner.ModelIdentity;

    public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken)
    {
        if (world.EmbeddingFailures > 0)
        {
            world.EmbeddingFailures--;
            throw new HttpRequestException("The embedding provider is unavailable.");
        }

        return inner.EmbedAsync(texts, cancellationToken);
    }
}
