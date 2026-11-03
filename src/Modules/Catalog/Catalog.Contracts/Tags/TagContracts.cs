namespace Catalog.Contracts.Tags;

public record CreateTagCommand(LocalizedText Name) : ICommand<CreateTagResult>;

public record CreateTagResult(Guid Id);

public record UpdateTagCommand(Guid Id, LocalizedText Name, bool IsActive = true, long? Version = null)
    : ICommand<UpdateTagResult>;

public record UpdateTagResult(bool Success, long Version);

/// <summary>Deactivates the tag (never a hard delete).</summary>
public record DeleteTagCommand(Guid Id) : ICommand<DeleteTagResult>;

public record DeleteTagResult(bool Success);

/// <summary>Inactive tags are returned only to admins who ask for them.</summary>
public record GetTagsQuery(bool IncludeInactive = false) : IQuery<List<TagDto>>;

public record TagDto
{
    public Guid Id { get; init; }
    public LocalizedText Name { get; init; } = default!;
    public string DisplayName { get; init; } = string.Empty;
    public bool IsActive { get; init; }
    public long Version { get; init; }
}
