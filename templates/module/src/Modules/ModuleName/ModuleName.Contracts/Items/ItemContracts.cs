using Shared.Contracts.CQRS;
using Shared.Contracts.Pagination;

namespace ModuleName.Contracts.Items;

/// <summary>Read model of an item. <see cref="Version"/> goes back on update for optimistic concurrency.</summary>
public record ItemDto(Guid Id, string Code, string Name, string? Description, DateTime? CreatedAt, long Version);

public record CreateItemCommand(string Code, string Name, string? Description) : ICommand<CreateItemResult>;

public record CreateItemResult(Guid Id);

/// <summary><see cref="Version"/>: the version read with the item; a stale one is rejected with 409.</summary>
public record UpdateItemCommand(Guid Id, string Name, string? Description, long? Version = null)
    : ICommand<UpdateItemResult>;

public record UpdateItemResult(long Version);

public record DeleteItemCommand(Guid Id) : ICommand<DeleteItemResult>;

public record DeleteItemResult(bool Deleted);

public record GetItemByIdQuery(Guid Id) : IQuery<ItemDto>;

/// <summary>Paged list, newest first; <see cref="Search"/> matches the code or the name.</summary>
public record GetItemsQuery : BaseQuery, IQuery<PaginatedResult<ItemDto>>
{
    public string? Search { get; init; }
}
