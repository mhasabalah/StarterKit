# In-app inbox, real-time (SignalR) and push (FCM)

Part of the [Notification module](Notification.md). Two **additive** channels sit next to the email/WhatsApp
fallback chain:

| Channel | What the user gets | Stored in | Delivered by |
|---|---|---|---|
| `InApp` | an inbox entry + a real-time event on every open tab/app | `notification.inbox_notification` | `InAppDeliveryJob` + `NotificationHub` |
| `Push` | an OS notification on every active device (Android, iOS, Web) | `notification.push_device` (the devices) | `PushDeliveryJob` → `IPushProvider` (FCM) |

"Additive" means they are delivered **in addition to** the chain, never instead of it: the inbox always gets the entry,
push follows the user's `pushEnabled` switch, and the chain keeps its own preferences and fallback. They can never
be put in a channel order (`ChannelOrderRules` and the `Notification:DefaultChannelOrder` validation refuse them).
Decision record: [ADR 0012](../Decisions/0012-realtime-inbox-and-push.md).

## Sending

```csharp
await notifications
    .For(invoice.UserId, culture)
    .WithEmail(EmailTemplate.InvoiceIssued, model.ToDictionary())          // the chain (optional)
    .WithInApp(new InAppContent(
        Title: new LocalizedText("Invoice ready", "الفاتورة جاهزة"),
        Body: new LocalizedText($"Invoice {invoice.Number} is ready.", $"الفاتورة {invoice.Number} جاهزة."),
        Category: "billing.invoice-issued",                                 // clients group / route by it
        ActionUrl: $"/billing/invoices/{invoice.Id}",                       // relative, or an allow-listed host
        Severity: NotificationSeverity.Success,
        Data: new Dictionary<string, string> { ["invoiceId"] = invoice.Id.ToString() }))
    .WithPush()                                                             // same title/body as the inbox entry
    .SendAsync(cancellationToken: ct);
```

- `WithPush(PushContent)` sends its own text; `WithPush(sensitive: true)` reuses the in-app text but sends a generic
  "New notification / Open the app to view it" (push content transits Google/Apple).
- Both need a USER recipient: `For(userId)` or `ToEmail(address, culture, userId)`. Without one they are logged
  `Skipped`.
- `SendAsync(NotificationChannel.Push)` (or `InApp`) delivers ONLY that channel. `SendAsync(NotificationChannel.Email)`
  narrows the chain and keeps in-app and push.
- Limits are part of the contract (`InAppContent.*MaxLength`, `MaxDataEntries`); exceeding the category or data limits
  throws at the call site, long text is truncated at delivery. `Data` is for ids and routes, never PII.

## Delivery

`NotificationJobEnqueuer` turns one notification into up to three Hangfire jobs (queue `notification`) that share ONE
correlation id: the chain (`NotificationDispatchJob`), `InAppDeliveryJob` and `PushDeliveryJob`. Separate jobs keep
failure domains apart: an FCM outage never re-runs (re-sends) the email, and an inbox `Sent` row never stops the email
(the chain only looks at its own rows).

**`InAppDeliveryJob`**

1. Inserts the entry with `INSERT ... ON CONFLICT (user_id, correlation_id) DO NOTHING`: a retry or a duplicate
   enqueue never creates a second entry.
2. Sends `NotificationReceived` + `UnreadCountChanged` to the user (all sessions), **then** writes the `Sent` log row.
   A crash in between re-sends the event on retry (clients de-duplicate by `id`); a finished delivery is a no-op.
3. Text is plain and capped; the action URL must be app-relative (`/x`, not `//x`) or an absolute http(s) URL on
   `Email:AppBaseUrl` / `Email:AllowedLinkHosts` (the email `safe_url` rule), otherwise it is dropped.
4. The entry is stamped with the tenant of the sending request (restored by the Hangfire tenant filter).

**`PushDeliveryJob`**

1. Skips (logged) when the user turned push off or has no device.
2. **Session-bound devices**: devices registered under a session (`sid`) that has since been signed out or revoked are
   deleted and skipped. The job asks Identity through `UserDirectory.Contracts` (`GetActiveSessionIdsQuery`), so a lost
   phone whose session an admin revoked stops receiving pushes at the next send.
3. Sends one multicast through `IPushProvider` (FCM: chunks of 500). Per device:

   | FCM result | Effect |
   |---|---|
   | delivered | counted |
   | `UNREGISTERED`, `SENDER_ID_MISMATCH` | the device is deleted (dead token) |
   | `UNAVAILABLE`, `INTERNAL`, `QUOTA_EXCEEDED`, network/5xx | retried by a follow-up job carrying ONLY those device ids (`Push:RetryDelaysSeconds`, up to `Push:MaxAttempts`) |
   | `INVALID_ARGUMENT`, `THIRD_PARTY_AUTH_ERROR` | logged, device kept, not retried (it may be the payload, so devices are never wiped on it) |

4. The correlation id is the collapse key on every platform (Android `tag`, APNs `apns-collapse-id`, Web Push `tag`),
   so a duplicate replaces the shown notification instead of stacking.
5. One `notification_log` row per attempt: `Sent` (at least one device accepted it; partial failures in `error`),
   `Skipped` or `Failed`. In-app and push rows cannot be re-enqueued by hand (they retry automatically).

## Real-time hub

`/hubs/notifications` (`NotificationHub`, `Notification.Contracts.Realtime.NotificationHubContract`). It is
**push-only**: clients invoke nothing on it, every command goes through REST. Events (camelCase JSON):

| Event | Payload | When |
|---|---|---|
| `NotificationReceived` | `InboxNotificationDto` | a new entry (may repeat after a retry: de-duplicate by `id`) |
| `UnreadCountChanged` | `{ tenantId, count }` | new entry, read, read-all, delete |
| `NotificationsRead` | `{ ids \| null, readAt, upTo }` | read on another tab/device (`ids: null` = all up to `upTo`) |
| `NotificationDeleted` | `{ id }` | deleted on another tab/device |

Events are hints, delivered best effort (a Redis hiccup is logged, never fails a request or job). **After every
(re)connect, re-read `GET /inbox/unread-count`.**

**Authentication.** Browsers cannot set `Authorization` on a WebSocket, so the token travels as
`?access_token=` - accepted ONLY under `/hubs` (`Shared.Core.Web.RealtimePaths`; anywhere else a token in the URL is
ignored). Every check (token blacklist, session revocation, security stamp) runs on the token that was validated,
however it arrived (`TokenBlacklistJwtBearerEvents`). API keys are refused (`DisallowApiKey`).
`CloseOnAuthenticationExpiration` drops the socket when the access token expires, so a revoked session cannot keep a
connection open for longer than one access-token lifetime (`JwtSettings:AccessTokenExpirationMinutes`, 15 in Docker);
the client reconnects with a fresh token.

**Scale-out.** With `Redis:Enabled` the hub uses the SignalR Redis backplane on its own connection (channel prefix
`<Redis:InstanceName>:signalr`): a send from the replica that ran the job reaches a socket on any replica. Behind a
load balancer without sticky sessions (the shipped nginx is `least_conn`), clients connect with **WebSockets and
`skipNegotiation: true`**; `appsettings.Docker.json` allows only WebSockets (`Realtime:Transports`). SSE/long polling
need sticky sessions.

### Browser client (TypeScript, `@microsoft/signalr`)

```ts
import { HubConnectionBuilder, HttpTransportType, LogLevel } from "@microsoft/signalr";

const hub = new HubConnectionBuilder()
  .withUrl("https://api.example.com/hubs/notifications", {
    accessTokenFactory: () => auth.getAccessToken(),   // called on every (re)connect: return a FRESH token
    transport: HttpTransportType.WebSockets,
    skipNegotiation: true,
  })
  .withAutomaticReconnect()
  .configureLogging(LogLevel.Warning)
  .build();

hub.on("NotificationReceived", (n) => inbox.upsert(n));          // de-duplicate by n.id
hub.on("UnreadCountChanged", (c) => badge.set(c.tenantId, c.count));
hub.on("NotificationsRead", (r) => inbox.markRead(r.ids, r.upTo));
hub.on("NotificationDeleted", (d) => inbox.remove(d.id));
hub.onreconnected(() => badge.refresh());                         // GET /api/v1/notifications/inbox/unread-count
hub.onclose(() => setTimeout(() => hub.start(), 5000));            // token expiry closes the socket by design

await hub.start();
await badge.refresh();
```

## Push devices (mobile / web app)

1. After sign-in, on every app start and whenever FCM rotates the token (`onTokenRefresh`):
   `PUT /api/v1/notifications/devices { token, platform: "Android" | "Ios" | "Web", appVersion?, locale? }`.
   It is an upsert by token: the device is bound to the CURRENT session; a token registered by another account moves
   to the caller (shared device). At most `Push:MaxDevicesPerUser` devices are kept (least recently seen evicted).
2. On sign-out, BEFORE discarding the session: `DELETE /api/v1/notifications/devices/{deviceId}`. (Forgetting it is
   safe: the revoked session drops the device at the next send.)
3. Devices not re-registered for `Push:StaleDeviceDays` are purged daily.
4. The data payload always carries `correlationId` (plus `category` / `actionUrl` with `WithPush()`): route on tap.

## Endpoints

| Route | Auth | Notes |
|---|---|---|
| `GET /api/v1/notifications/inbox?unreadOnly=&category=&pageNumber=&pageSize=` | the user | newest first, the current tenant + platform notices |
| `GET /api/v1/notifications/inbox/unread-count` | the user | badge |
| `POST /api/v1/notifications/inbox/{id}/read` | the user | idempotent |
| `POST /api/v1/notifications/inbox/read-all { upTo? }` | the user | pass the `createdAt` of the newest entry the user SAW |
| `DELETE /api/v1/notifications/inbox/{id}` | the user | |
| `PUT /api/v1/notifications/devices` | the user, no API key | register / refresh |
| `GET /api/v1/notifications/devices` | the user, no API key | token masked (`...abcdefgh`) |
| `DELETE /api/v1/notifications/devices/{id}` | the user, no API key | |
| `PUT /api/v1/notifications/preferences { channelOrder, pushEnabled? }` | the user | `pushEnabled` omitted = unchanged |
| `/hubs/notifications` | the user (JWT, header or `?access_token=`), no API key | SignalR |

A foreign or unknown id is always 404 (never 403), so ids cannot be probed.

## Tenancy

The inbox is per user. With multi-tenancy on, a request in tenant T sees T's entries plus tenant-less platform
notices (`InboxScope`); single-tenant mode sees all. Entries are deleted with the tenant (`TenantDataPurgeRequestedEvent`)
and with the user (`UserDeletedIntegrationEvent`, which also deletes the devices). Hub events carry `tenantId`; a client
in another tenant ignores them or refreshes.

## Configuration

| Section | Keys (defaults) |
|---|---|
| `Inbox` | `RetentionDays` (90) |
| `Realtime` | `Enabled` (true; false = no hub, clients poll), `Transports` (comma-separated, `WebSockets,ServerSentEvents,LongPolling`; Docker: `WebSockets`), `KeepAliveInterval` (15s), `ClientTimeoutInterval` (30s), `UseRedisBackplane` (true) |
| `Push` | `Provider` (`Auto` = FCM when credentials exist, else NoOp; `Fcm`; `NoOp`), `ProjectId`, `CredentialsFile` or the `fcm_service_account_json` secret, `DefaultTimeToLive` (1 day, max 28), `MaxAttempts` (4), `RetryDelaysSeconds` ([30,120,600]), `MaxDevicesPerUser` (10), `StaleDeviceDays` (60) |

Everything is validated at startup (`ValidateOnStart`): `Push:Provider=Fcm` without credentials, an unknown transport
or a bad interval refuses to boot. `Realtime:Transports` is a string, not an array: configuration merges arrays by
index, so an environment file could never remove a transport from one.

## FCM / APNs setup

1. Create a Firebase project; add the Android, iOS and/or Web apps (the client SDKs give the registration token).
2. iOS: upload the APNs authentication key (`.p8`) in Firebase → Project settings → Cloud Messaging. Pushes to iOS go
   through FCM; there is no separate APNs adapter.
3. Generate a service-account key (Project settings → Service accounts) with only the "Firebase Cloud Messaging API
   Admin" role, save it as `secrets/fcm_service_account.json` and run with the overlay:
   `docker compose -f docker-compose.yml -f docker-compose.push.yml up -d`. The key is read by path
   (`Push:CredentialsFile`) and never loaded into configuration (`.json` secrets are read-by-path, like `.pfx`).
4. Only a `service_account` key is accepted (an external-account JSON could make the SDK call arbitrary token URLs).

## Operations

- Metrics (meter `StarterKit.Notification`): `notification.inapp.delivered`, `notification.push.sent{result}`,
  `notification.realtime.sent{method,result}`; open sockets: `signalr.server.active_connections`
  (meter `Microsoft.AspNetCore.Http.Connections`).
- Delivery history: `GET /api/v1/notifications/history?channel=Push&status=Failed`.
- Nginx: `location /hubs/` (upgrade headers, no buffering, 3600s read timeout, its own per-IP connection budget, a log
  format without the query string) - [Nginx](../deployment/Nginx.md).

## Tests

| Where | What |
|---|---|
| `Notification.Tests/Dispatch/AdditiveChannelDispatchTests.cs` | job fan-out, forced channels, the chain is never stopped by an inbox row, builder rules, Hangfire argument round trip |
| `Notification.Tests/Dispatch/InAppDeliveryJobTests.cs` | idempotency, crash-and-retry, unsafe links, cultures, truncation |
| `Notification.Tests/Dispatch/PushDeliveryJobTests.cs` | session-bound devices, dead tokens, retry only the failed devices, attempts exhausted, sensitive text, TTL |
| `Notification.Tests/Dispatch/PushProviderTests.cs` | the FCM error table, the FCM message mapping (collapse keys), credential type |
| `Notification.Tests/Persistence/InboxAndDevicesPostgresTests.cs` | real Postgres: `ON CONFLICT`, read-all `upTo`, tenant scope, ownership, device upsert/move/cap, purge, erasure |
| `Notification.Tests/Realtime/NotificationHubTests.cs` | real hub + .NET client: per-user targeting, isolation, auth, push-only hub, **Redis backplane across two replicas** |
| `Identity.Tests/Sessions/HubTokenTests.cs` | query-string token only on `/hubs`, a logged-out token is rejected from the query string |
| `Api.E2ETests/Notification/InboxAndRealtimeE2ETests.cs` | real login → hub over WebSockets → notification received; logout → hub refused; REST endpoints |
| `Notification.Tests/Dispatch/FcmLiveTests.cs` | **opt-in, real Firebase**: a real device token is delivered, an unknown one never is |
| `Api.E2ETests/Notification/FcmLiveE2ETests.cs` | **opt-in, real Firebase**: login → register device over HTTP → push through FCM → logout → next push skipped, device deleted |

The two live tests are skipped unless configured (CI skips them). To run them, get a device token (for a browser:
any page that calls the Firebase JS SDK `getToken` with your project's web config and VAPID key), then:

```bash
export FCM_CREDENTIALS_FILE=secrets/fcm_service_account.json FCM_TEST_TOKEN="<device token>"
export Push__Provider=Fcm Push__CredentialsFile="$FCM_CREDENTIALS_FILE"
dotnet test src/Modules/Notification/Notification.Tests --filter "FullyQualifiedName~FcmLiveTests"
dotnet test src/Api.E2ETests --filter "FullyQualifiedName~FcmLiveE2ETests"
```

The device shows "StarterKit push works" and "StarterKit: delivered through the API", and never the third message
(sent after logout).
