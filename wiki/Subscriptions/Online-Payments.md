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
   | `Refunded` | session `Refunded` + a warning log; **no automatic ledger entry**, reconcile through the refund flow |
   | `Other` | stored and marked processed |

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
    "Fake": { "Enabled": true, "Secret": "CHANGE_ME-at-least-16-chars", "BaseUrl": "https://pay.fake.example.com" }
  },
  "PaymentWebhooks": { "MaxAttempts": 5, "RetryBackoffMinutes": 2, "RetryCron": "*/5 * * * *" }
}
```

A gateway works only when its adapter is registered **and** `Enabled` is true; an enabled gateway without `Secret`
fails startup. Secrets come from user-secrets, environment variables or Docker secrets, never from committed files.
`Fake` is for dev and tests: never enable it in production.

## The Fake adapter (dev, tests, template)

Webhook body: `{ "id": "evt_1", "type": "payment.succeeded|payment.failed|refund.created", "reference": "<session id>",
"amount": 100.00, "currency": "SAR" }`, header `X-Fake-Signature: hex(HMAC-SHA256(secret, rawBody))`. Any other `type`
is normalized to `Other`.

## Adding a real provider (Stripe, Paymob, Tap, Moyasar)

1. Add `Domains/OnlinePayments/Gateways/<Name>PaymentGateway.cs` implementing `IPaymentGateway` (internal). Copy
   `FakePaymentGateway`:
   - `Name`: the route and config name (`Stripe`).
   - `CreateCheckoutAsync`: call the provider API (a typed `HttpClient` registered in `AddSubscriptionsModule`), send our
     `PaymentId` as the client reference, return the provider session id and hosted-page URL. The amount comes from the
     request, never from the client.
   - `ParseWebhook`: verify the provider signature scheme over the **raw body** in constant time
     (`CryptographicOperations.FixedTimeEquals`), reject stale timestamps if the scheme has them, then map the provider
     event to `PaymentWebhookEvent` (`EventId` = the provider unique event id, `ProviderReference` = the session id
     returned by checkout, amount in major units with 2 decimals, upper-case currency). Never throw for bad input.
     Event types you do not need map to `Other`.
2. Register it next to the Fake one in `SubscriptionsModule`:
   `services.AddScoped<IPaymentGateway, StripePaymentGateway>();`.
3. Enable it in config: `Subscriptions:PaymentGateways:Stripe:Enabled/Secret/BaseUrl`.
4. Add adapter unit tests (valid, tampered, wrong secret, missing header, malformed, unknown type) like
   `FakePaymentGatewayTests`. The integration tests already exercise the provider-neutral path.
5. Point the provider dashboard webhook at `https://<host>/api/v1/webhooks/payments/Stripe`.

## Notes and limits

- The inbox stores the verified raw body (jsonb) plus the normalized fields, so retries need no headers.
- Amounts are 2 decimals (module-wide `numeric(18,2)`); a webhook with more decimals is rejected as malformed.
- `successUrl` and `cancelUrl` only have to be absolute http(s) URLs; restrict them to your frontend host in the adapter
  if the provider does not.
- Online refunds are not automated; the `Refunded` event is recorded and logged only.
