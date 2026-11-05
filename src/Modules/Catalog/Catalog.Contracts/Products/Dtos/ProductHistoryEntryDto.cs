using System.Text.Json;

namespace Catalog.Contracts.Products.Dtos;

/// <summary>
/// One recorded change of a product. <see cref="Changes"/> is <c>{ "Property": { "old": ..., "new": ... } }</c>
/// (<c>old</c> absent on creation, <c>new</c> absent on hard delete); secrets are never recorded.
/// </summary>
public record ProductHistoryEntryDto(
    Guid Id,
    string Action,
    JsonElement Changes,
    Guid? ActorUserId,
    Guid? ImpersonatorUserId,
    string? CorrelationId,
    string? IpAddress,
    DateTime OccurredAt);
