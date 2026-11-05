namespace Webhooks.Contracts.Endpoints;

/// <summary>An endpoint as clients see it. The secret is NEVER part of it (only returned on create / rotate).</summary>
public sealed record WebhookEndpointDto(
    Guid Id,
    string Url,
    string? Description,
    IReadOnlyList<string> EventTypes,
    bool Enabled,
    int ConsecutiveFailures,
    DateTime? DisabledAt,
    string? DisabledReason,
    DateTime? LastSuccessAt,
    DateTime? LastFailureAt,
    DateTime CreatedAt,
    long Version);

/// <summary>One subscribable event type (the wire name of a <c>[PublicWebhookEvent]</c> integration event).</summary>
public sealed record WebhookEventTypeDto(string Type, string? Description);

// ----- commands --------------------------------------------------------------------------------------------------

/// <param name="EventTypes">Wire names from the catalog; <c>*</c> subscribes to every public event.</param>
public sealed record CreateWebhookEndpointCommand(string Url, string? Description, IReadOnlyList<string> EventTypes)
    : ICommand<CreateWebhookEndpointResult>;

/// <param name="Secret">The signing secret (<c>whsec_...</c>). Shown ONCE; store it on the receiving side.</param>
public sealed record CreateWebhookEndpointResult(Guid Id, string Secret);

/// <summary>Re-enabling an auto-disabled endpoint (Enabled = true) resets its failure counter.</summary>
public sealed record UpdateWebhookEndpointCommand(
    Guid Id,
    string Url,
    string? Description,
    IReadOnlyList<string> EventTypes,
    bool Enabled,
    long? Version = null) : ICommand<UpdateWebhookEndpointResult>;

public sealed record UpdateWebhookEndpointResult(bool Success, long Version);

public sealed record DeleteWebhookEndpointCommand(Guid Id) : ICommand<DeleteWebhookEndpointResult>;

public sealed record DeleteWebhookEndpointResult(bool Success);

/// <summary>Issues a new signing secret; the old one stops working immediately.</summary>
public sealed record RotateWebhookEndpointSecretCommand(Guid Id) : ICommand<RotateWebhookEndpointSecretResult>;

public sealed record RotateWebhookEndpointSecretResult(Guid Id, string Secret);

/// <summary>Queues a signed <c>webhook.ping</c> delivery to the endpoint.</summary>
public sealed record SendWebhookTestPingCommand(Guid Id) : ICommand<SendWebhookTestPingResult>;

public sealed record SendWebhookTestPingResult(Guid DeliveryId);

// ----- queries ---------------------------------------------------------------------------------------------------

public sealed record GetWebhookEndpointsQuery : BaseQuery, IQuery<PaginatedResult<WebhookEndpointDto>>;

public sealed record GetWebhookEndpointByIdQuery(Guid Id) : IQuery<WebhookEndpointDto>;

public sealed record GetWebhookEventTypesQuery : IQuery<IReadOnlyList<WebhookEventTypeDto>>;
