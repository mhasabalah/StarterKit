# 0009. Deliver public integration events to tenants as signed outbound webhooks

Opt-in public integration events reach tenant-managed webhook endpoints through signed, retryable deliveries.

## Status

- **Status:** Accepted
- **Date:** 2026-11-05

## Context

Tenants need to react to platform events in their own systems (a product went live, a subscription expired).
Integration events already exist (ADR-0004), but they are internal: at-least-once on the bus, no external contract,
no signing, no retries toward a third party. Calling arbitrary tenant-supplied URLs from the server is also the
classic SSRF vector (cloud metadata at `169.254.169.254`, internal admin ports, DNS rebinding).

## Decision

A new tenant-data module, **Webhooks**, forwards a curated subset of integration events to tenant-registered URLs.

- **Opt-in catalog.** An integration event is public only when marked `[PublicWebhookEvent]` (`Shared.Contracts`).
  The customer-facing event type is its `[MessageUrn]` name (`catalog.product-published.v1`). The catalog is built from
  `IntegrationEventTypes.Scan` and intersected with the events the module actually forwards.
- **Forwarding.** One tiny sealed consumer per public event derives from `WebhookForwardingConsumer<TEvent>` (MassTransit
  discovers only closed types). It creates one `WebhookDelivery` per subscribed, enabled endpoint of the tenant, idempotent
  per `(endpoint, event id)` through a unique index, then enqueues the delivery job after commit. Subscriptions events carry
  no tenant header (the subscriber is the tenant), so those consumers name the tenant explicitly.
- **Wire format.** [Standard Webhooks](https://www.standardwebhooks.com): headers `webhook-id` (stable across retries and
  redeliveries), `webhook-timestamp`, `webhook-signature: v1,base64(HMAC-SHA256(secret, "id.timestamp.body"))`, secret
  `whsec_...`. The signature is recomputed with a fresh timestamp on every attempt. Verified against the reference vector.
- **Secrets.** Encrypted at rest with ASP.NET Core Data Protection (purpose `Webhooks.EndpointSecret`). The raw secret is
  returned only by create and rotate.
- **Retries.** One layer only: a Hangfire job makes one attempt and schedules the next with exponential backoff
  (10 s x 6^n, capped at 6 h, 8 attempts). The delivery row is the source of truth; it is leased by moving `NextAttemptAt`
  under optimistic concurrency, so duplicate jobs cannot both send. A recurring sweep re-enqueues deliveries whose job was
  lost. Polly is deliberately not used: the host adds a 3-retry resilience handler to every `IHttpClientFactory` client,
  so the sender owns a single long-lived guarded `HttpClient` instead.
- **Failure policy.** Timeouts, connection errors, 408/425/429 and 5xx are retried; other 4xx, redirects and blocked
  addresses fail permanently. Every failed attempt counts against the endpoint; at 10 consecutive failures it is disabled
  and the tenant owner is emailed (domain event, after commit). Manual redelivery and test pings are tenant-permissioned
  (`webhooks.manage`).
- **SSRF guard (the part that matters).** `SocketsHttpHandler.ConnectCallback` resolves DNS itself, rejects the host if
  ANY resolved address is non-public, and connects to the checked IP (no second resolution, so no rebinding window).
  Blocked: loopback, RFC 1918, link-local/metadata, CGNAT `100.64/10`, `0.0.0.0/8`, multicast/reserved, IPv6 loopback,
  ULA `fc00::/7`, link-local `fe80::/10`, and IPv6 forms that embed an IPv4 address (IPv4-mapped, IPv4-compatible,
  NAT64, 6to4, Teredo). No proxy, no redirects, 10 s timeout, 64 KB response cap. URL registration additionally requires
  https and rejects credentials, literal private IPs and internal host names. `Webhooks:AllowPrivateNetworks` and
  `Webhooks:RequireHttps=false` exist for local development only; the module refuses to start with them outside
  Development/Testing.
- **Retention.** A nightly per-tenant job deletes finished deliveries older than 30 days.

## Consequences

- Tenants get an at-least-once, signed, observable (delivery log, redeliver) integration channel.
- Marking an event `[PublicWebhookEvent]` is a public API commitment: additive evolution only. A Webhooks unit test fails
  if a marked event has no forwarding consumer.
- Webhook payloads exist in two places (outbox/bus and `webhook_deliveries`); both are purged by their own retention.
- Receivers must de-duplicate on `webhook-id`.
- Not done: secret rotation with an overlap window (old secret dies immediately), per-endpoint rate limiting, and a
  per-host circuit breaker.

## Alternatives considered

- **Forward every integration event.** Leaks internal events and freezes them as a public contract. Rejected.
- **Polly retries on a named `HttpClient`.** Double retries with the host defaults, no per-attempt bookkeeping. Rejected.
- **Validate the URL only at registration.** Defeated by DNS rebinding and later DNS changes. Rejected; the connect-time check is
  authoritative and registration checks are only early feedback.
- **Resolve DNS and then let the handler re-resolve.** Leaves a TOCTOU window. Rejected.

## Related

[Webhooks](../Modules/Webhooks.md).
