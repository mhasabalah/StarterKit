using Webhooks.Domains.Endpoints.Events;
using Webhooks.Domains.Endpoints.Exceptions;

namespace Webhooks.Domains.Endpoints.Models;

/// <summary>
/// A tenant's webhook subscription: where to POST, which events, and the (Data-Protection encrypted) signing secret.
/// The URL is validated by <c>WebhookUrlPolicy</c> (SSRF rules need options) BEFORE reaching the aggregate; the
/// connection-time guard in the HTTP sender is the authoritative check.
/// </summary>
public class WebhookEndpoint : Aggregate<Guid>, IMustHaveTenant
{
    public const int UrlMaxLength = 2048;
    public const int DescriptionMaxLength = 500;
    public const int MaxEventTypes = 50;
    public const int DisabledReasonMaxLength = 500;

    /// <summary>Subscribes to every public event type (including future ones).</summary>
    public const string AllEvents = "*";

    /// <summary>Owning tenant (stamped on insert by TenantEntityInterceptor; immutable).</summary>
    public Guid TenantId { get; set; }

    public string Url { get; private set; } = default!;
    public string? Description { get; private set; }
    public string[] EventTypes { get; private set; } = [];

    /// <summary>The signing secret, protected with ASP.NET Core Data Protection (purpose <c>Webhooks.EndpointSecret</c>).</summary>
    public string SecretProtected { get; private set; } = default!;

    public bool Enabled { get; private set; }
    public int ConsecutiveFailures { get; private set; }
    public DateTime? DisabledAt { get; private set; }
    public string? DisabledReason { get; private set; }
    public DateTime? LastSuccessAt { get; private set; }
    public DateTime? LastFailureAt { get; private set; }

    private WebhookEndpoint()
    {
    }

    public static WebhookEndpoint Create(string url, string? description, IEnumerable<string> eventTypes,
        string secretProtected, Guid? id = null)
    {
        var endpoint = new WebhookEndpoint
        {
            Id = id ?? Guid.NewGuid(),
            Enabled = true,
            SecretProtected = RequireSecret(secretProtected)
        };
        endpoint.Configure(url, description, eventTypes);
        return endpoint;
    }

    /// <summary>Changes the target / subscription. Re-enabling an auto-disabled endpoint resets its failure counter.</summary>
    public void Update(string url, string? description, IEnumerable<string> eventTypes, bool enabled, DateTime now)
    {
        Configure(url, description, eventTypes);
        if (enabled)
        {
            if (!Enabled)
                Enable();
        }
        else
        {
            Disable("Disabled manually.", now);
        }
    }

    public void Enable()
    {
        Enabled = true;
        ConsecutiveFailures = 0;
        DisabledAt = null;
        DisabledReason = null;
    }

    /// <summary>Manual disable (no owner notification: the caller is the one who did it).</summary>
    public void Disable(string reason, DateTime now)
    {
        if (!Enabled)
            return;
        Enabled = false;
        DisabledAt = now;
        DisabledReason = Clip(reason, DisabledReasonMaxLength);
    }

    public void RotateSecret(string secretProtected) => SecretProtected = RequireSecret(secretProtected);

    public bool IsSubscribedTo(string eventType) =>
        Array.IndexOf(EventTypes, AllEvents) >= 0 || Array.IndexOf(EventTypes, eventType) >= 0;

    public void RecordSuccess(DateTime now)
    {
        ConsecutiveFailures = 0;
        LastSuccessAt = now;
    }

    /// <summary>
    /// Counts a failed attempt. Returns <c>true</c> when this failure disabled the endpoint (limit reached), in which
    /// case <see cref="WebhookEndpointAutoDisabledEvent"/> is raised.
    /// </summary>
    public bool RecordFailure(DateTime now, int disableAfter, string reason)
    {
        ConsecutiveFailures++;
        LastFailureAt = now;

        if (!Enabled || disableAfter <= 0 || ConsecutiveFailures < disableAfter)
            return false;

        Enabled = false;
        DisabledAt = now;
        DisabledReason = Clip(
            $"Disabled automatically after {ConsecutiveFailures} consecutive failures. Last error: {reason}",
            DisabledReasonMaxLength);
        AddDomainEvent(new WebhookEndpointAutoDisabledEvent(Id, Url, ConsecutiveFailures, reason));
        return true;
    }

    private void Configure(string url, string? description, IEnumerable<string> eventTypes)
    {
        if (string.IsNullOrWhiteSpace(url) || url.Trim().Length > UrlMaxLength)
            throw new InvalidWebhookEndpointException($"Url is required and must not exceed {UrlMaxLength} characters.");

        var types = (eventTypes ?? []).Select(t => t?.Trim() ?? string.Empty).Where(t => t.Length > 0)
            .Distinct(StringComparer.Ordinal).ToArray();
        if (types.Length == 0)
            throw new InvalidWebhookEndpointException("At least one event type is required.");
        if (types.Length > MaxEventTypes)
            throw new InvalidWebhookEndpointException($"At most {MaxEventTypes} event types are allowed.");

        Url = url.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : Clip(description, DescriptionMaxLength);
        EventTypes = types;
    }

    private static string RequireSecret(string secretProtected) =>
        string.IsNullOrWhiteSpace(secretProtected)
            ? throw new InvalidWebhookEndpointException("A signing secret is required.")
            : secretProtected;

    private static string Clip(string value, int max) => value.Length <= max ? value : value[..max];
}
