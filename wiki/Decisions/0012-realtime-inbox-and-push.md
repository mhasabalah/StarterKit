# 0012. Deliver in-app and push as additive Notification channels over SignalR and FCM

## Status

- **Status:** Accepted
- **Date:** 2026-11-05

## Context

Backlog G13 (in-app inbox + real-time) and G18 (mobile push + device registration). The Notification module already
owned every delivery (email, WhatsApp) through one sender, one Hangfire pipeline and one delivery log, built as a
**fallback chain**: the first channel that succeeds wins. An inbox entry or a push is not an alternative to an email,
it comes in addition to it. The API runs as several replicas behind `least_conn` nginx (no sticky sessions); access
tokens are short-lived JWTs whose sessions can be revoked (`sid`).

## Decision

1. **Additive channels.** `NotificationChannel.InApp` and `Push` are delivered alongside the chain, never as part of
   it (they cannot appear in a channel order). Senders attach them with `.WithInApp(...)` / `.WithPush(...)` on the
   same `INotificationSender`. Users switch push off with `pushEnabled`; the inbox is always written.
2. **One job per channel group**, sharing one correlation id: `NotificationDispatchJob` (chain), `InAppDeliveryJob`,
   `PushDeliveryJob`. Failure domains stay apart, and each job is idempotent on its own rows (the chain ignores
   in-app/push rows).
3. **Inbox exactly once, events at least once.** A unique `(user_id, correlation_id)` and `ON CONFLICT DO NOTHING`
   make the entry exactly-once; the real-time event may repeat and clients de-duplicate by id.
4. **SignalR, push-only hub**, `/hubs/notifications`, addressed by user id. Commands stay REST (validation, tenancy,
   concurrency in the mediator pipeline). Events are best effort; the inbox table is the source of truth.
5. **Redis backplane** on a dedicated connection when Redis is on. Without sticky sessions, clients use WebSockets with
   `skipNegotiation`.
6. **Hub auth = API auth.** The token may come from `?access_token=` only under `/hubs`; every revocation check runs on
   the validated token (this closed a gap where the blacklist read only the `Authorization` header). Sockets close at
   token expiry (`CloseOnAuthenticationExpiration`). API keys are refused.
7. **FCM for every platform** (Android, iOS through the APNs key in Firebase, Web) behind `IPushProvider`, with a
   NoOp default. FirebaseAdmin is confined to Notification (`ThirdPartyIsolationTests`).
8. **Session-bound devices.** A device stores the registering `sid`; before each send the job drops devices whose
   session ended (`GetActiveSessionIdsQuery` in `UserDirectory.Contracts`), so revoking a session also stops its pushes.
   Retries resend only to transiently failed devices; the correlation id is the platform collapse key.

## Consequences

- Any module adds an inbox entry or push in one line; no new module boundary and no new message type.
- The `INotificationBuilder` interface grew; hand-written test fakes implement three more methods.
- Real-time freshness, not data, is lost on a Redis outage (clients resync over REST after reconnecting).
- A revoked session's socket survives at most one access-token lifetime.
- Multi-replica deployments must keep WebSockets end to end (nginx `location /hubs/`), or add sticky sessions to use
  SSE/long polling.
- Enforced by: `AdditiveChannelDispatchTests`, `InAppDeliveryJobTests`, `PushDeliveryJobTests`,
  `InboxAndDevicesPostgresTests`, `NotificationHubTests` (incl. the Redis backplane), `HubTokenTests`,
  `InboxAndRealtimeE2ETests`, `ThirdPartyIsolationTests`.

## Alternatives considered

- **InApp/Push as chain channels**: they would be skipped whenever email succeeded; wrong semantics.
- **One job for all channels**: an FCM outage would re-run (and re-send) the email, and per-channel retry policies
  would be impossible.
- **A separate Realtime module**: the inbox is a delivery channel with the same log, preferences and erasure; a module
  boundary would duplicate them.
- **Server-Sent Events only / polling**: no bidirectional transport needed, but SignalR gives reconnect, fallbacks,
  user addressing and a supported Redis backplane out of the box.
- **Direct APNs + FCM adapters**: two providers, two credentials, two error tables for no gain while FCM relays to
  APNs. Kept possible behind `IPushProvider` (backlog follow-up).
- **A `UserSessionRevokedIntegrationEvent` to delete devices**: needs an outbox write from Identity's session code;
  re-reading the active sessions at send time is simpler and never stale.

## Related

[Notification Realtime and Push](../Modules/Notification-Realtime-and-Push.md).
