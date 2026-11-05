# Online payments: gateway port and webhook inbox

Billing was manual (proof upload, admin approval). Online payments add a provider-neutral port and an idempotent
webhook inbox. **Money logic is not duplicated**: a verified `payment.succeeded` event records a receipt on the payment
and runs `IPaymentSettlementService.SettleAsync` (activate / renew / proration, invoice, ledger `Payment` entry), exactly
like a manual receipt that completes a payment. See [Billing.md](Billing.md).

## Flow

1. `POST /api/v1/subscriptions/{subscriptionId}/online-payment` `{ provider, successUrl, cancelUrl, paymentId? }`
   (signed-in subscriber). The owed payment is resolved like a manual receipt (explicit id, or the single owed payment
   for the subscription status). The gateway creates a hosted checkout for the payment's **outstanding amount** and an
   `online_payment_sessions` row links `(provider, provider_reference)` to the payment. Response: `redirectUrl`.
2. The subscriber pays at the provider. Nothing is settled yet.
3. The provider calls `POST /api/v1/webhooks/payments/{provider}` (anonymous, rate limited by IP via the `webhook`
   policy, raw body capped at 256 KB):
   - the adapter verifies the signature over the **raw body**: failure = 401, malformed = 400, unknown or disabled
     provider = 404; **nothing is stored** in those cases;
   - the event is inserted into `subscriptions.inbound_webhook_events` with `ON CONFLICT (provider, event_id) DO NOTHING`.
     A duplicate returns 200 `{ duplicate: true }` and is never reprocessed;
   - a new event is processed inline in its own transaction, then 200 is returned. A processing failure is recorded on
     the row and never turns into a non-2xx, so the provider does not hammer us.
4. Processing by event type:

   | Type | Effect |
   |---|---|
   | `PaymentSucceeded` | settles the payment (below); session `Succeeded` |
   | `PaymentFailed` | session `Failed`; the payment stays owed, the subscriber may start a new checkout |
   | `Refunded` | books the refund (see [Refunds](#refunds)): credit notes + one `Refund` ledger entry per provider refund id |
   | `Other` | stored and marked processed |

## Refunds

Two ways money goes back, both ending in the same books (credit notes + a negative `Refund` ledger entry) and both
**idempotent per provider refund id** (`online_payment_refunds`, unique `(provider, provider_refund_id)`).

- **The provider reports a refund** (`Refunded` event, e.g. a refund made in the Stripe / Paymob dashboard). The event
  carries the provider refund id and the amount of THAT refund (the Fake adapter takes `refund_id`, default the event
  id). The processor (own transaction, session row-locked):
  1. a refund id that was already booked is a no-op (new event id, or the echo of an admin refund);
  2. the session must be settled, the currency must match, the amount must be positive;
  3. `Σ refunds of the session + this refund` may **never exceed the money received** (`session.Amount`); otherwise the
     event is rejected permanently (row `Failed`, nothing booked);
  4. credit notes are issued over the subscription's invoices (newest first, same allocation as the manual flow), one
     `Refund` ledger entry (`LedgerEntry.ForProviderRefund`, reference = provider refund id) is added, the session
     becomes `Refunded` only when nothing is left to refund (a partial refund keeps it `Succeeded`).
  A later refund request for the subscription sees these refunds as already gone (`RefundInputs.RefundableAsync`).
- **An admin refund of an online payment** (`MarkRefundProcessed`). Before the books are touched, the handler calls
  `IPaymentGateway.RefundAsync` for the settled online sessions of the subscription (newest first, never more than a
  session received, idempotency key `refund-<request>-<session>-<refunded so far>-<amount>`). Each provider refund is
  saved at once (so a crash or a later failure never loses the fact that money left; a retry subtracts it), then the
  usual processing runs and the **provider refund ids replace the admin reference**. The provider then echoes the same
  refund by webhook: recognized by id, not booked again. If the webhook got there first, the request adopts that
  refund (`BookedByWebhook`) and books only the rest. A provider failure (`PaymentGatewayException`, 502
  `payment_gateway_error`) leaves the request `Approved` with nothing booked. A stale `Version` is refused before the
  provider is called. Money received by manual payment is the admin's off-line transfer, as before.

## Settlement checks (`PaymentSucceeded`)

The event is **rejected permanently** (row `Failed`, attempts exhausted at once, logged, nothing settled) when: the
provider reference is unknown; amount or currency differs from the session; the payment is no longer owed (already Paid,
for example an admin recorded it meanwhile, so the money must be refunded manually); the outstanding amount changed;
`EnsureSettleable` refuses (wrong subscription status or currency). A second success event for an already settled
session (new event id) is a no-op. Any other exception (database, concurrency) is **transient**: the row is `Failed`
with attempts left and the job retries it.

## Retry job

`retry-failed-payment-webhooks` (Hangfire, default every 5 minutes, `Subscriptions:PaymentWebhooks:RetryCron`) picks
`Failed` rows with `attempts < MaxAttempts` whose last attempt is older than `attempts x RetryBackoffMinutes`, 50 per
run, each in its own `IUnitOfWorkRunner` unit of work. Rows that run out of attempts stay `Failed` for an admin.

## Admin

`GET /api/v1/admin/subscriptions/webhook-events/failed?provider=&pageNumber=&pageSize=` (`billing.read`): failed events
with `lastError`, `attempts`, amount and reference. Resolve them by hand (record a manual payment, refund at the
provider); there is no re-drive endpoint.

## Configuration

```json
"Subscriptions": {
  "PaymentGateways": {
    "Fake": { "Enabled": true, "Secret": "CHANGE_ME-at-least-16-chars", "BaseUrl": "https://pay.fake.example.com" },
    "Stripe": { "Enabled": false, "ApiKey": "CHANGE_ME_sk_...", "Secret": "CHANGE_ME_whsec_...", "WebhookToleranceSeconds": 300 },
    "Paymob": { "Enabled": false, "ApiKey": "CHANGE_ME", "PublicKey": "CHANGE_ME", "Secret": "CHANGE_ME_hmac",
                "IntegrationIds": [123456], "BaseUrl": "https://accept.paymob.com", "NotificationUrl": null }
  },
  "PaymentWebhooks": { "MaxAttempts": 5, "RetryBackoffMinutes": 2, "RetryCron": "*/5 * * * *" }
}
```

A gateway works only when its adapter is registered **and** `Enabled` is true; an enabled gateway without `Secret`
fails startup (Stripe also needs `ApiKey`; Paymob needs `ApiKey`, `PublicKey` and `IntegrationIds`). `Secret` only
verifies inbound webhooks, `ApiKey` authenticates our outbound calls; both come from env / Docker secrets. Secrets come from user-secrets, environment variables or Docker secrets, never from committed files.
`Fake` is for dev and tests: never enable it in production.

## The Fake adapter (dev, tests, template)

Webhook body: `{ "id": "evt_1", "type": "payment.succeeded|payment.failed|refund.created", "reference": "<session id>",
"amount": 100.00, "currency": "SAR" }`, header `X-Fake-Signature: hex(HMAC-SHA256(secret, rawBody))`. Any other `type`
is normalized to `Other`.

## Stripe adapter

`StripePaymentGateway` (Stripe.net). Checkout: a hosted Checkout Session in `payment` mode (`client_reference_id` = our
payment id, amount in minor units with the currency exponent: 0 / 2 / 3 decimals). Webhook: Stripe's own scheme
(`Stripe-Signature: t=...,v1=...`, HMAC-SHA256 over `t.rawBody`, constant-time) through
`EventUtility.ValidateSignature` with `WebhookToleranceSeconds` (default 300) against `TimeProvider`: a wrong
signature, a malformed header and a **stale timestamp** are all `401`. The verified JSON is then read directly (no
dependence on the SDK's pinned API version). Subscribe the endpoint (`/api/v1/webhooks/payments/Stripe`) to:

| Stripe event | Mapped to |
|---|---|
| `checkout.session.completed` (`payment_status=paid`) / `checkout.session.async_payment_succeeded` | `PaymentSucceeded`; the `payment_intent` is remembered on the session |
| `checkout.session.expired` / `checkout.session.async_payment_failed` | `PaymentFailed` |
| `refund.created` / `refund.updated` with `status=succeeded` | `Refunded`, keyed by the refund id (`re_...`), against the `payment_intent` |
| `charge.refunded`, `payment_intent.payment_failed`, anything else | `Other` |

`charge.refunded` is deliberately not mapped: it only carries a cumulative total and no refund id, so mapping it next
to the `refund.*` events would book one refund twice. `payment_intent.payment_failed` is not mapped because a failed
card attempt does not end a Checkout Session (the customer can retry); only expiry does. API refunds use
`RefundService` on the remembered PaymentIntent with the idempotency key. Outbound calls use an `IHttpClientFactory`
client (`stripe`) with timeouts and circuit breaker only: **retries are off** (a retried POST must not double-charge).

## Paymob adapter

`PaymobPaymentGateway` (typed `HttpClient` `paymob`, retries off like `WhatsAppApiClient`). Checkout: Intention API
(`POST /v1/intention/`, `Authorization: Token <ApiKey>`, amount in cents, `special_reference` unique per attempt);
the **intention's order id** is our session id and the subscriber is sent to
`/unifiedcheckout/?publicKey=...&clientSecret=...`. Point the integrations' *transaction processed callback* at
`/api/v1/webhooks/payments/Paymob` (or set `NotificationUrl`).

Paymob does not sign the body: it appends `?hmac=`, the hex **HMAC-SHA512** (key = the HMAC secret) of 20 fields of
`obj` concatenated in lexicographic order (`amount_cents, created_at, currency, error_occured,
has_parent_transaction, id, integration_id, is_3d_secure, is_auth, is_capture, is_refunded, is_standalone_payment,
is_voided, order.id, owner, pending, source_data.pan, source_data.sub_type, source_data.type, success`; booleans as
`true`/`false`), compared in constant time. `WebhookRequest.Query` exists for this. **Only signed fields are trusted**:
the session is found by `order.id`, amount / currency / ids come from signed fields, `parent_transaction` is never read.

| Callback | Mapped to |
|---|---|
| `success`, not pending / voided / refunded / child | `PaymentSucceeded`; the transaction id is remembered (the refund API needs it) |
| not `success`, not pending | `PaymentFailed` |
| child transaction (`has_parent_transaction`) with `success` + `is_refunded` | `Refunded`, refund id = its transaction id (the refund API returns the same id) |
| pending, voided, auth-only, parent updated after a refund | `Other` |

Paymob has no event id, so the event id is `txn:<id>:<state flags>`: a pending callback never shadows the final one.
Refund API: `POST /api/acceptance/void_refund/refund` `{ transaction_id, amount_cents }`. The `billing_data` of an
intention is filled with `NA` (the hosted page collects the real data).

## Adding a real provider (Stripe, Paymob, Tap, Moyasar)

1. Add `Domains/OnlinePayments/Gateways/<Name>PaymentGateway.cs` implementing `IPaymentGateway` (internal). Copy
   `FakePaymentGateway`:
   - `Name`: the route and config name (`Stripe`).
   - `CreateCheckoutAsync`: call the provider API (a typed `HttpClient` registered in `AddSubscriptionsModule`), send our
     `PaymentId` as the client reference, return the provider session id and hosted-page URL. The amount comes from the
     request, never from the client.
   - `ParseWebhook(WebhookRequest)`: the request carries the raw body, headers AND query string (Paymob signs in
     `?hmac=`). Verify the provider signature scheme over the **raw body** in constant time
     (`CryptographicOperations.FixedTimeEquals`), reject stale timestamps if the scheme has them, then map the provider
     event to `PaymentWebhookEvent` (`EventId` = the provider unique event id, `ProviderReference` = the session id
     returned by checkout, amount in major units with 2 decimals, upper-case currency). Never throw for bad input.
     Event types you do not need map to `Other`. Refund events set `ProviderRefundId` (the idempotency key) and the
     amount of that one refund; a success event sets `PaymentReference` (the provider payment id, remembered on the
     session for later refunds).
   - `RefundAsync`: refund up to `Amount` of the settled payment and return the provider refund id. Throw
     `PaymentGatewayException` when the provider refuses. Use the request's `IdempotencyKey` where the provider has one.
2. Register it next to the Fake one in `SubscriptionsModule`:
   `services.AddScoped<IPaymentGateway, MyGateway>();` (+ a named `HttpClient` with
   `.AddStandardResilienceHandler(o => o.Retry.ShouldHandle = _ => ValueTask.FromResult(false))` if it calls an API).
3. Enable it in config: `Subscriptions:PaymentGateways:Stripe:Enabled/Secret/BaseUrl`.
4. Add adapter unit tests (valid, tampered, wrong secret, missing header, malformed, unknown type) like
   `FakePaymentGatewayTests`. The integration tests already exercise the provider-neutral path.
5. Point the provider dashboard webhook at `https://<host>/api/v1/webhooks/payments/Stripe`.

## Notes and limits

- The inbox stores the verified raw body (jsonb) plus the normalized fields, so retries need no headers.
- Amounts are 2 decimals (module-wide `numeric(18,2)`); a webhook with more decimals is rejected as malformed.
- `successUrl` and `cancelUrl` only have to be absolute http(s) URLs; restrict them to your frontend host in the adapter
  if the provider does not.
- Stripe / Paymob are verified offline (signature fixtures and a stubbed `HttpMessageHandler`); live calls need
  sandbox keys per project.
- Automatic card retries and saved payment methods are out of scope.
