namespace Tenancy.Domains.FeatureFlags.Exceptions;

/// <summary>Invalid flag data (400).</summary>
public class InvalidFeatureFlagException(string message) : DomainException(message);

public class FeatureFlagNotFoundException(string key) : NotFoundException($"The feature flag '{key}' was not found.");

public class FeatureFlagOverrideNotFoundException(string key, Guid overrideId)
    : NotFoundException($"The override {overrideId} of feature flag '{key}' was not found.");

public class FeatureFlagAlreadyExistsException(string key) : ConflictException($"The feature flag '{key}' already exists.");
