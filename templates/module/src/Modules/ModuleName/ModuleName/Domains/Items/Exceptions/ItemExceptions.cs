namespace ModuleName.Domains.Items.Exceptions;

public sealed class ItemNotFoundException(Guid id) : NotFoundException("Item", id);

public sealed class ItemAlreadyExistsException(string message) : ConflictException(message);

/// <summary>An aggregate invariant was violated (validators normally catch it first): 400.</summary>
public sealed class InvalidItemException(string field, string? value)
    : BadRequestException($"Invalid item {field}: '{value}'.");
