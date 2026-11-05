namespace Webhooks.Domains.Endpoints.Exceptions;

public sealed class WebhookEndpointNotFoundException(Guid id) : NotFoundException("WebhookEndpoint", id);

public sealed class WebhookEndpointAlreadyExistsException(string url)
    : ConflictException($"A webhook endpoint for '{url}' already exists.");

public sealed class InvalidWebhookEndpointException(string message) : BadRequestException(message);
