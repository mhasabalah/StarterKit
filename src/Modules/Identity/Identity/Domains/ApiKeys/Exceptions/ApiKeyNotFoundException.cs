namespace Identity.Domains.ApiKeys.Exceptions;

public sealed class ApiKeyNotFoundException(Guid keyId)
    : NotFoundException($"API key '{keyId}' was not found.");
