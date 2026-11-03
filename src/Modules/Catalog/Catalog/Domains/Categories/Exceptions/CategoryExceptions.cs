namespace Catalog.Domains.Categories.Exceptions;

public class CategoryNotFoundException(Guid id) : NotFoundException("Category", id);

public class InvalidCategoryException(string message) : BadRequestException(message);

public class CategorySlugAlreadyExistsException(string slug)
    : ConflictException($"A category with slug '{slug}' already exists.");
