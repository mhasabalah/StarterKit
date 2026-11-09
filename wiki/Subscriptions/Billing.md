# Subscriptions billing: money rules

Reference for how the Subscriptions module charges, invoices, renews, upgrades and refunds.
Code: `src/Modules/Subscriptions/Subscriptions/Domains/Subscriptions/Billing/` (pure policies, unit-tested in
`Subscriptions.Tests/Billing`) and `Services/` (issuer, orchestrator, proof submission).

## Lifecycle

```
PendingApproval ──approve──► Active ──renewal boundary──► PastDue ──renewal approved──► Active
       │                       │                             │
       └─reject─► Rejected     └─cancel─► Cancelled ◄────────┤
                                                             └─grace elapsed─► Expired
```

- One enum, `Subscriptions.Contracts.Subscriptions.Enums.SubscriptionStatus`, with stable numbers
  (PendingApproval=0, Active=1, PastDue=2, Expired=3, Cancelled=4, Rejected=5, Trialing=6 appended). Stored by **name** in the database.
- Allowed transitions live in `SubscriptionStateMachine`.
- **At most one open subscription per subscriber** (PendingApproval/Active/PastDue): filtered unique index
  `ux_subscriptions_subscriber_open`. A unique violation (Postgres `23505`) becomes a **409** in
  `SubscriptionsDbContext.SaveChangesAsync`. Approval never cancels another subscription.

## Currency and decimals

Billing money is stored as `numeric(19,4)` (Catalog product prices stay `(18,2)`; see ADR 0017 for the migration lock and
the lossy `Down`) and rounded in the domain per ISO 4217 currency: 0 decimals (JPY, KRW), 2 (USD,
SAR, EGP), 3 (KWD, BHD, OMR, JOD, TND), 4 (CLF). `Currency` is the one helper (`Round`, `HasValidScale`,
`ToMinorUnits`, `Format`); plans and coupons require a real ISO code and an amount that fits it; provider amounts
(Stripe, Paymob) use the same minor-unit rules. See ADR
[0017](../Decisions/0017-money-precision-and-per-currency-rounding.md). Never `Round(x, 2)` or `ToString("F2")` money.

## Sandbox and live gateways

Each `Subscriptions:PaymentGateways:<Name>` entry has `Mode` = `Sandbox` (default) or `Live`. The host refuses to boot
when a key contradicts the mode (`sk_test_` / `rk_test_` / `egy_sk_test_` with Live, the `live` forms with Sandbox, also
the Paymob public key) and, in Production, when a sandbox gateway (the Fake adapter too) is enabled without
`AllowSandboxInProduction=true`.

**Upgrade note.** A Production deployment that runs a live key and sets no `Mode` now refuses to boot, because the default
is Sandbox. Set `Subscriptions:PaymentGateways:<Name>:Mode=Live` (and keep test keys for staging with `Mode=Sandbox`).

Getting sandbox keys per project:

- **Stripe.** Dashboard in test mode: Developers > API keys gives `sk_test_...` (`ApiKey`). Create a webhook endpoint
  to `/api/v1/webhooks/payments/Stripe` (events listed in `StripePaymentGateway`, plus `payment_intent.succeeded` and
  `payment_intent.payment_failed` when auto-retry is on) and copy its `whsec_...` into `Secret`; locally,
  `stripe listen --forward-to` prints one. Test card `4242 4242 4242 4242`; `4000 0025 0000 3155` asks for 3-D Secure.
- **Paymob.** A test-mode account gives test secret/public keys and integration ids; the same host serves both modes
  (`BaseUrl` only for a regional host). Point the integration callback at `/api/v1/webhooks/payments/Paymob`.
- Keep real values in user-secrets, environment variables or Docker secrets; `CHANGE_ME` in committed files.

## Tax (`Subscriptions:Tax`)

| Key | Default | Meaning |
|-----|---------|---------|
| `Subscriptions:Tax:PricesIncludeTax` | `true` | Plan prices are gross (tax-inclusive). `false` = prices are net, tax is added. |
| `Subscriptions:Tax:Rate` | `0.15` | Decimal fraction, `0..1` (validated at startup). `0.075` = 7.5%. |

`TaxCalculator` is the only place tax is computed (2 decimals, midpoint away from zero; parts always sum):

- Inclusive: `net = round(gross / (1 + r))`, `tax = gross - net`.
- Exclusive: `net = price`, `tax = round(net * r)`, `gross = net + tax`.

Payments are always **gross** (what the subscriber transfers). Invoices snapshot net/tax/gross/rate/PricesIncludeTax at
issue time; changing the configuration never changes an issued invoice. Rates print without truncation (`7.5%`).

## Prices (`PricingPolicy`)

- Effective plan price at a moment = the scheduled price once its effective date passed, then `DiscountPercent`.
  **`Plan.DiscountPercent` is applied to charges.**
- Renewal price follows the plan only when the plan is still compatible (same currency and billing cycle, not deleted);
  otherwise the subscription keeps its own price snapshot.
- Renewal bill = period price + deferred upgrade proration, converted to gross.

## Billing periods (`BillingPeriod`)

- Period = `[CurrentPeriodStart, RenewalDate)`. Renewal dates are computed from the activation date (the anchor), so
  month-end clamping never drifts: Jan 31 → Feb 28/29 → Mar 31 → Apr 30.
- `Renew()` starts the new period at the **previous renewal date**, not "now": a late payment or a slow admin never
  shifts the cycle.
- All day math is `decimal` on the actual period length (no `double`, no 30-day months).

## Renewal boundary (`RenewalOrchestrator`)

Hourly job `process-past-due` (`ProcessPastDueSubscriptionsJob`) processes every Active subscription whose renewal date
passed, each in its own scope, in this order:

1. end-of-period cancellation → Cancelled (nothing is billed);
2. scheduled downgrade → applied (status-guarded: Active only);
3. still Active → PastDue + a **Renewal** payment priced at the effective price (+ deferred proration, which is then
   cleared).

`process-expired` expires PastDue subscriptions after the grace period, **except** when a renewal proof is Submitted
(under review). The former `process-scheduled-cancellations` / `process-scheduled-downgrades` jobs were merged into the
orchestrator; their recurring ids are removed at startup.

Renewal approval binds to the renewal **payment** and approves its amount (never the live plan price).

## Payments and approvals

- Each payment has a purpose: `Activation`, `Renewal`, `Proration`.
- Proof uploads (activation, renewal, proration) share `ProofSubmissionService`.
- Admin approve/reject commands carry `PaymentId`. Without it, the single submitted payment of that purpose is used;
  several submitted → **400**, none → **409**.
- Critical admin commands accept `Version` (optimistic concurrency → 409): subscription version for activation/renewal
  review, payment version for proration review, refund version for refund review, original invoice version for credit
  notes. Read DTOs expose `Version`.

## Upgrades (immediate, prorated)

`POST /subscriptions/{id}/upgrade`

- Same currency and billing cycle only (400 otherwise). A pending scheduled downgrade is cancelled.
- Proration = price difference × remaining share of the **current** period.
- Trusted subscriber (≥ `TrustMinCycles` paid activation/renewal payments) and proration ≤
  `ProratedDeferralThresholdPercent` of the current price → **deferred**: added to the next renewal bill.
- Otherwise a **Proration** payment and its invoice are created with the upgrade (same transaction):
  1. subscriber uploads proof: `POST /subscriptions/{id}/proration/proof` (form: `file`, `paymentId`, `paymentMethodId`);
  2. admin: `POST /admin/subscriptions/{id}/proration/approve` `{ paymentId, version? }` or
     `/proration/reject` `{ paymentId, reason, version? }` (subscriber may re-upload until the SLA).
- `process-upgrade-sla-expiry`: after `UpgradeSlaHours`, a proration payment that is still **Pending or Rejected** is
  closed (Failed), the subscription reverts to the original plan, and **only that payment's invoice** gets a credit note.
  Submitted proofs (awaiting review) and paid invoices are never touched. Past-due subscriptions are left for an admin.
- A new upgrade is refused while a previous upgrade is still unpaid.

## Downgrades

`POST /subscriptions/{id}/schedule-downgrade` schedules a change for the renewal date. A subscription has many
`ScheduledPlanChange` rows over time; at most one is Pending (`ux_scheduled_plan_changes_subscription_pending`).

## Invoices and credit notes

- The invoice row (number, description, buyer snapshot from `ISubscriberInfoProvider`, seller snapshot, plan, period,
  tax snapshot) is inserted in the **same transaction** as the approval / renewal / upgrade.
- Numbers: `{Prefix}-{yyyy}-{seq:D6}` (e.g. `INV-2026-000042`), gapless per year from `invoice_number_counters`
  (`SELECT ... FOR UPDATE` in the issuing transaction; a rollback releases the number). Credit notes use the same
  sequence. `Subscriptions:Invoice:NumberPrefix` max 20 characters.
- PDF + email run afterwards in `InvoiceDeliveryJob` (Hangfire, retried, idempotent by invoice id). The PDF renders
  from the snapshot only. `process-invoice-retry` re-enqueues deliveries that were never queued or failed.
- Issued invoices are immutable. Corrections are credit notes (`Type = CreditNote`, negative amounts,
  `OriginalInvoiceId`): `POST /admin/invoices/{id}/credit-note` `{ reason, amount?, version? }`, up to the creditable
  remainder. One invoice per payment (`ux_subscription_invoices_payment`).

## Refunds

`POST /subscriptions/{id}/refund` (cancelled annual subscriptions):

- refundable = Σ Paid payments − Σ (Approved + Processed) refunds; nothing refundable → 409;
- based on money received for the current period and when service actually ended (`EndDate`, else `CancelledAt`):
  full refund within `RefundFullWindowDays` of the period start, prorated (unused days / period days) within
  `RefundProratedWindowDays`; always capped at refundable;
- the cap is re-checked on approval; marking a refund processed issues credit notes for the refunded amount;
- one non-rejected refund per subscription (index includes Processed), so a second refund is impossible.
- refunds of money received ONLINE go back through the payment gateway at `MarkRefundProcessed`, and refunds the
  provider reports on its own (dashboard) are booked from its webhook, never above the money received and once per
  provider refund id; see [Online-Payments.md](Online-Payments.md#refunds).

## Settlement (one completion path)

`PaymentSettlementService` is the ONLY way a payment completes, shared by proof approvals (activation, renewal,
proration) and manual payments: `EnsureSettleable` (status for the purpose: Activation → PendingApproval,
Renewal → PastDue, Proration → Active; same currency) → the caller marks the payment Paid → `SettleAsync`
(activate / renew + transition log, subscriber snapshot refresh, invoice issued or the existing proration invoice
found, ledger Payment entry for the amount received by this step) → after commit `AfterCommitAsync` (cache eviction,
invoice delivery). Everything before the commit is one transaction.

## Ledger (immutable money ledger)

`ledger_entries` (`Domains/Ledger/Models/LedgerEntry`) is the single source for billing KPIs and exports.

| Type | Written by | Amount |
|------|-----------|--------|
| `Payment` | proof approval (activation/renewal/proration), each manual receipt, approved-application activation | `+` amount received by that step (an approval after partial receipts books only the outstanding rest) |
| `Refund` | `MarkRefundProcessed` | `-` refunded amount (the credit notes it issues are documentation and are NOT booked again) |
| `CreditNote` | standalone `IssueCreditNote` | `-` credit note total |
| `Adjustment` | comp (complimentary activation) | `0` (any sign allowed for future manual adjustments) |

Each row snapshots currency, subscription/subscriber, payment/invoice/refund ids, actor, plan id/name, payment method
and reference. Amounts are gross, `decimal(18,2)`, signed; the sum per currency is the net money.

- **Same transaction**: the entry is added to the same `SubscriptionsDbContext` before the one `SaveChanges` of the
  money event, so a failed approval (stale version, invoice failure, ...) leaves no ledger row (integration-tested).
- **Append-only in code**: no setters or mutators; `ILedgerRepository` has only `Add`; every property is
  `PropertySaveBehavior.Throw` after save; `SubscriptionsDbContext` refuses Modified/Deleted `LedgerEntry` (and
  `SubscriptionPaymentReceipt`) entries before SaveChanges. Corrections = a new compensating entry.
- **Optional DB-level guard** (not generated by EF; add it to a migration or run it once per environment). It also
  blocks `ExecuteUpdate`/`ExecuteDelete` and raw SQL:

```sql
CREATE OR REPLACE FUNCTION subscriptions.ledger_entries_append_only() RETURNS trigger AS $$
BEGIN
  RAISE EXCEPTION 'ledger_entries is append-only (% refused)', TG_OP;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER ledger_entries_no_update_delete
  BEFORE UPDATE OR DELETE ON subscriptions.ledger_entries
  FOR EACH ROW EXECUTE FUNCTION subscriptions.ledger_entries_append_only();
-- Optionally: REVOKE UPDATE, DELETE, TRUNCATE ON subscriptions.ledger_entries FROM <app role>;
```

No backfill: the template starts empty. A project adopting this on existing data must backfill Payment entries from
Paid payments and Refund entries from Processed refunds first, or the KPIs undercount history.

## Online payments (gateway + webhook)

A subscriber can pay a pending activation / renewal / proration payment through a payment provider instead of
uploading a proof. The provider's signed webhook settles it through the same path as a manual receipt. Flow, config,
failure handling and how to add a real provider: [Online-Payments.md](Online-Payments.md).

## Manual / partial payments

`POST /admin/subscriptions/{id}/payments/manual` `{ amount, method, reference?, receivedAt?, note?, paymentId?, version? }`
(`billing.approve`) records money received offline against the payment owed in the current status (activation /
renewal / proration; `paymentId` disambiguates).

- Each call adds an insert-only `subscription_payment_receipts` row and a ledger Payment entry for that amount.
- Below the amount due: payment `PartiallyPaid` (`AmountReceived`, `OutstandingAmount = Amount - AmountReceived`).
  A partially paid payment is still owed (`IsAwaitingPayment`), may still get a proof (approval books only the
  outstanding rest), a rejected proof returns it to `PartiallyPaid`, and the expiry job does not expire a PastDue
  subscription with a partially paid renewal (an admin decides, like for a submitted proof).
- The receipt that reaches the amount due settles the payment exactly like a proof approval (activation/renewal,
  invoice for the full amount, ledger) in the same transaction.
- Overpayment: 400 unless `Subscriptions:ManualPayments:AllowOverpaymentCredit = true`; then the excess stays recorded
  as received money (settle it with a refund; it is not applied automatically).
- The subscription row is always touched, so `version` (subscription) is enforced and concurrent receipts serialize.
- Self-approval (admin = subscriber) is refused for every admin money action.

## Admin lifecycle actions

| Endpoint (`billing.approve`) | Rule |
|---|---|
| `POST /admin/subscriptions/{id}/extend` `{ days 1-366, reason, version? }` | Active: renewal date + days (a scheduled end-of-period cancellation, a pending downgrade and a comp end move with it). PastDue: grace period + days. Other statuses 409. Transition log + audit. |
| `POST /admin/subscriptions/{id}/reactivate` `{ reason, complimentary?, months?, version? }` | Cancelled/Expired that ended within `Subscriptions:ReactivationWindowDays` (default 30): → PendingApproval with a NEW activation payment at the current plan price (`PricingPolicy.RenewalPeriodPrice`), approved through the normal proof/manual path with a fresh billing anchor; or, `complimentary`, straight to Active for `months`. Another open subscription of the subscriber → 409 (unique index). |
| `POST /admin/subscriptions/{id}/comp` `{ planId, months 1-36, reason, version? }` | PendingApproval (or reactivatable Cancelled/Expired) → Active on `planId` for `months` months; owed payments are waived (Failed); a Paid 0-amount activation payment gets a 0-total invoice and a ledger Adjustment of 0. `ComplimentaryUntil` marks the period (excluded from MRR, cleared at the next renewal boundary, which bills normally). |

Reopening Cancelled/Expired is an admin exception outside the state-machine table
(`SubscriptionStateMachine.CanReactivate`); the regular transitions stay terminal.

## Duplicate proof detection

On every proof upload `ProofSubmissionService` reads the attachment's SHA-256 (`AttachmentDto.ContentHash`, via
`GetAttachmentByIdQuery`), stores it as `SubscriptionPayment.ProofContentHash` and looks for another payment with the
same hash. A match sets `DuplicateProof` + `DuplicateOfPaymentId` and logs a warning; it never blocks. The flag is
shown in the pending queue (`duplicateProofOnly` filter), the Subscriber-360 and the approval audit (`after_json`).
A missing hash (older files, Attachment unavailable) only skips the check.

## Admin console (read side)

| Endpoint | Permission | Notes |
|---|---|---|
| `GET /admin/subscriptions` | `billing.read` | Filters: `status`, `planId`, `currency`, `search` (ILIKE on the subscriber name/email snapshot + plan name, or an exact subscription/subscriber id), `renewalFrom/To` [from, to), `minAmount/maxAmount` (price snapshot); shared paging + `sortColumn` (any scalar, e.g. `RenewalDate`, `Price`, `SubscriberDisplayName`). All in SQL. |
| `GET /admin/subscriptions/{id}` | `billing.read` | Subscriber-360: subscription, payments (receipts, proof attachment id, content hash, duplicate flag), invoices, credit notes, refunds, transition log, scheduled changes, ledger + totals per currency, plan limits (`GetActivePlanLimitsQuery`). Usage counters are not tracked by this module. |
| `GET /admin/payments/pending` | `billing.read` | Queue rows now carry subscriber name/email, plan, `ageHours`, duplicate flag, received/outstanding. Filters: `method`, `paymentMethodId`, `purpose`, `submittedFrom/To`, `minAmount/maxAmount`, `search`, `duplicateProofOnly`. |
| `GET /admin/billing/ledger` | `billing.read` | Paged; `from/to`, `type`, `subscriberId`, `subscriptionId`, `currency`. |
| `GET /admin/billing/dashboard` | `billing.read` | See below. |

Subscriber search uses denormalized `subscriptions.subscriber_display_name` / `subscriber_email`, refreshed from
`ISubscriberInfoProvider` on create and on every activation/renewal/comp/reactivation. They are display/search
copies, never authoritative. For large tables consider a `pg_trgm` GIN index on both columns (contains-search
cannot use the plain b-tree index).

### Billing dashboard

`GET /admin/billing/dashboard?from=&to=` - range [from, to) UTC, default the current calendar month.

- MRR/ARR per currency: Σ price / cycle months over Active, non-complimentary subscriptions (price in the configured
  tax basis); ARR = MRR × 12. Currencies are never summed together.
- Counts by status, past-due count, proof queue size + oldest age (hours), churn (Cancelled + Expired with an end date
  in range), new subscriptions (activated in range).
- Revenue per currency from the LEDGER: gross (Payment), refunds, credit notes, adjustments, net (= signed sum),
  grouped also by plan and by payment method (`Unspecified` when the entry has none, e.g. refunds).
- Cached per range by `IBillingDashboardCache` for `Subscriptions:BillingDashboardCacheMinutes` (default 5, 0
  disables it). Invalidation is explicit: `BillingDashboardCacheInvalidationHandler` drops EVERY cached range after a
  ledger entry, a payment review change or a subscription status change commits.
  - Every key embeds a generation token (`subscriptions:billing-dashboard:generation`, in Redis, so all replicas
    agree); invalidating writes a new token. The TTL stays as the safety net.
  - Ledger writes are covered in one place: `ILedgerRepository.Add` stages a `LedgerEntryRecordedEvent` on the same
    save, so it fires only when the entry commits (credit notes and provider refunds included).
  - A cache failure is logged and never fails the request or the money event.
- `/dashboard/stats` (host) adds MRR, queue size and past-due count when the caller holds `billing.read`.

## Admin notifications

"New payment proof", "refund requested" and the daily digest go to `Subscriptions:AdminNotifications:Emails` when
set; otherwise to the active users of `Subscriptions:AdminNotifications:Roles` (default `Admin`, `Finance`),
resolved with Identity.Contracts `GetUsersQuery { Role }`. Never to `Email:FromAddress` (the sender). Nobody
resolved → a warning is logged and nothing is sent.

Daily job `admin-billing-digest` (`AdminBillingDigestJob`, cron `Subscriptions:AdminNotifications:DigestCron`,
default `0 7 * * *` UTC; email template `AdminBillingDigest`): proofs awaiting review, how many are older than
`DigestStaleProofHours` (default 24), the oldest proof age, past-due and partially paid counts. Nothing to report = no
email. It runs through `IUnitOfWorkRunner` like every job.

## Permissions

Admin billing endpoints require permission policies (`Shared.Core.Authorization.Permissions`, registered by Identity),
not roles:

| Permission | Endpoints |
|---|---|
| `billing.read` | pending subscriptions/payments, past-due, payment by id, admin payment proof, admin invoices list, refund requests list, admin subscription search + 360, ledger, billing dashboard |
| `billing.approve` | approve/reject activation, renewal, proration; manual payment; extend; reactivate; comp; admin cancel; invoice retry/resend |
| `billing.refund` | refund review, mark refund processed, credit note |
| `plans.manage` | plan CRUD, plan features, feature definitions/catalog, admin plan reads, price change schedule/cancel/force |
| `payment-settings.manage` | payment methods (incl. the admin list) and payment instructions |
| `audit.read` | `GET /superadmin/subscriptions/audit` (was SuperAdmin-only) |
| `billing.export` | reserved for exports (none yet) |
| `billing.coupons.manage` | coupon CRUD (`/admin/coupons`); Finance, Admin, SuperAdmin |

`GET /invoices/{id}/pdf` treats a caller with the `billing.read` permission claim (or the Admin/SuperAdmin role) as
staff.

## Audit

Every admin financial action writes `admin_action_audit_logs` (with `amount`, `currency`, `before_json`,
`after_json`) in the same unit of work: plan CRUD and price changes, plan features, payment methods and instructions,
approvals/rejections (activation, renewal, proration; `after_json` includes `DuplicateProof`), manual payments,
extend, reactivate, comp, admin cancel, refund review/processing, invoice retry/resend/credit note.

## Configuration (new keys)

| Key | Default | Meaning |
|-----|---------|---------|
| `Subscriptions:ReactivationWindowDays` | `30` | How long after the end a subscription can be reactivated / comped. |
| `Subscriptions:BillingDashboardCacheMinutes` | `5` | Dashboard cache TTL per range (0 = no cache). |
| `Subscriptions:ManualPayments:AllowOverpaymentCredit` | `false` | Accept manual receipts above the outstanding amount. |
| `Subscriptions:AdminNotifications:Emails` | (empty) | Explicit admin recipients (array). |
| `Subscriptions:AdminNotifications:Roles` | `["Admin","Finance"]` | Roles resolved when `Emails` is empty. |
| `Subscriptions:AdminNotifications:DigestStaleProofHours` | `24` | "Stale proof" threshold of the digest. |
| `Subscriptions:AdminNotifications:DigestCron` | `0 7 * * *` | Digest schedule (UTC). |

## Schema changes (Wave 4b - generate the migration)

`SubscriptionsDbContext` (no migration was generated):

- `subscriptions`: `subscriber_display_name varchar(200) null`, `subscriber_email varchar(256) null`,
  `complimentary_until timestamptz null`; indexes on `plan_id`, `subscriber_email`.
- `subscription_payments`: `proof_content_hash varchar(64) null` (indexed), `duplicate_proof bool not null default
  false`, `duplicate_of_payment_id uuid null`, `amount_received numeric(18,2) not null default 0`; status value
  `PartiallyPaid = 5` (int column, no DDL).
- new `subscription_payment_receipts` (FK `payment_id` → `subscription_payments`, restrict).
- new `ledger_entries` (indexes: `occurred_at`, `(currency, occurred_at)`, `subscription_id`, `subscriber_id`,
  `payment_id`, `refund_id`). Add the optional trigger above to the migration if wanted.

```bash
dotnet ef migrations add AdminBillingConsole --context SubscriptionsDbContext \
  --project src/Modules/Subscriptions/Subscriptions --startup-project src/Api --output-dir Data/Migrations
```

## Tests

- `Subscriptions.Tests`: tax, billing periods, pricing, refunds, credit notes, invoice numbers, state machine,
  domain lifecycle, dashboard math (MRR mix, currencies, net), partial payments/overpayment, ledger entry rules,
  duplicate-proof flag, extend/comp/reactivate rules.
- `Subscriptions.IntegrationTests` (Testcontainers Postgres, real module wiring): concurrent create/approval, gapless
  numbers under 20 parallel approvals, refund cap and double refund, upgrade → proof → approval, SLA revert,
  renewal orchestrator ordering, repeated downgrades, PDF snapshot after a plan rename; `AdminBillingTests`: SQL-side
  search + paging, Subscriber-360, ledger written in the same transaction (none on failure, append-only), manual
  partial payments to activation/renewal, comp/reactivate/extend, duplicate proofs, pending-queue filters, dashboard
  over a seeded ledger + caching, admin notification routing, digest job.

## Billing growth: trials, coupons, dunning, metering

### Free trials

- `Plan.TrialDays` (0 = no trial, max 365). `POST /api/v1/subscriptions/trial {planId}` starts a **Trialing**
  subscription at once: full access, no payment, no approval. `SubscriptionActivatedIntegrationEvent` is raised, so
  Tenancy treats the subscriber as active. Trialing counts as the subscriber's one open subscription.
- **One trial per subscriber, ever**: `Subscription.TrialStartedAt` is kept after the trial; a second attempt is a 409
  with `errorCode = trial_already_used` (`plan_has_no_trial` when the plan offers none).
- The trial end is the renewal date. `RenewalOrchestrator` handles it at the boundary (`TrialEnded`): the first paid
  period is billed as a **Renewal** payment (effective price, less a coupon), the subscription goes PastDue with
  `Subscriptions:Trials:PostTrialGraceDays` (3) to pay, and the billing anchor restarts at the trial end. Paying
  runs the normal renewal approval (→ Active); not paying expires it. A trial is cancelled immediately (nothing paid).
- `process-trial-reminders` (daily 09:30 UTC, `Trials:ReminderCron`) sends the `TrialEnding` email
  `Trials:ReminderDaysBeforeEnd` (3) days before the end; `TrialReminderSentAt` makes it exactly-once.

### Coupons

- `Coupon` (code unique and case-insensitive, `Percent` or `FixedAmount`, duration `Once` / `Repeating(n)` /
  `Forever`, `MaxRedemptions`, `ExpiresAt`, allowed plan ids) and `CouponRedemption` (unique per coupon + subscriber).
  Terms are immutable after creation. Admin CRUD: `/api/v1/admin/coupons` (`billing.coupons.manage`). A coupon that
  was redeemed can only be deactivated.
- Redeem: with the subscription (`CreateSubscriptionCommand.CouponCode`, discounts the activation bill) or later on
  the running subscription (`POST /subscriptions/me/coupons/redeem`, `.WithIdempotency()`, discounts the next bills).
  Failures carry `errorCode`: `coupon_inactive`, `coupon_expired`, `coupon_exhausted`, `coupon_plan_not_allowed`,
  `coupon_currency_mismatch`.
- Pricing: the coupon applies to the period price after the plan `DiscountPercent`, before tax, never to the deferred
  proration, and never below 0.01 per charge. `Subscription.Price` stays the list price; the discounted amount is on
  the payment (`CouponCode`, `CouponDiscountAmount`) and snapshotted on the invoice. The subscription snapshots the
  coupon terms and counts down `CouponPeriodsRemaining` per discounted bill.
- The last redemption is serialized by the coupon aggregate's optimistic `Version` (the loser gets a 409).
  A redemption is not given back if the subscription is later rejected.

### Dunning

`process-dunning` (hourly :20, `Dunning:Cron`) reminds subscribers whose renewal is PastDue: at
`Subscriptions:Dunning:ScheduleDays` (default `[1,3,5]`) after past due, each with a fresh hosted checkout link
(first enabled gateway; the renewal page when none works), plus a **final** notice
`FinalNoticeDaysBeforeGraceEnd` (1) day before the grace period ends. Each step is a `dunning_notices` row, unique per
payment + step, written before the email, so it never re-sends. Several steps due at once (job was down): only the
latest is sent, the rest are recorded as superseded. A renewal whose proof is under review is not nagged.
With `Dunning:AutoRetry:Enabled` each step first tries one automatic card charge (see
[Saved cards and automatic retries](#saved-cards-and-automatic-retries)).

### Saved cards and automatic retries

Off by default (`Subscriptions:Dunning:AutoRetry:Enabled`). Design and reasons: ADR
[0018](../Decisions/0018-off-session-dunning-retries-on-saved-cards.md).

- **Saving (opt-in).** Only when the operator enabled auto-retry AND the subscriber sent `saveCard: true` on the
  online-payment request AND the gateway supports it (Stripe, Fake), the hosted checkout asks to keep the card
  (`setup_future_usage=off_session`) and reuses the subscriber's existing provider customer. After the payment settles,
  the first card becomes the default `saved_payment_methods` row (provider references, brand, last4, expiry,
  `consented_at`). Dunning's own links never save a card.
- **Managing.** `GET /api/v1/subscriptions/saved-payment-methods`, `POST .../{id}/default`, `DELETE .../{id}`. The
  subscriber (tenant billing permission in multi-tenant mode) only; no impersonation, no API keys. Removing revokes
  locally, then detaches at the provider.
- **Retrying.** Before a step's email: one off-session charge of the default card. `payment_attempts` is unique per
  payment + step; the provider idempotency key is `dunning:{paymentId}:{step}`. Success settles through the webhook
  path (invoice, ledger) and sends no reminder. 3-D Secure needed, declined, expired card or provider outage: the usual
  email with a fresh checkout link, the reason in the email (en/ar), and, only when a retry was really attempted, an
  in-app notice (`billing.payment-failed`). An earlier step whose charge may still be completing is reconciled first and
  blocks a new charge; the reminder then has no checkout link. A charge that succeeded but could not be settled raises an
  `OffSessionChargeUnsettled` admin audit entry (ADR 0018). The dunning notice records the retry outcome.
- **Fake gateway outcomes** (dev/tests): a saved method reference containing `declined`, `action` or `transient`
  fails that way; anything else succeeds.

### Metering and quotas

- Ports in `Shared.Core.Entitlements`: `IUsageMeter` (`RecordAsync`, `GetUsageAsync`) and `IQuotaService`
  (`TryConsumeAsync`), with permissive defaults. Endpoint filter `.RequireQuota("key", qty)` answers **429** with
  `errorCode = quota_exceeded` (usage is counted per attempt).
- `PlanFeature.Limit` + `ResetPeriod` (`Monthly` = calendar month UTC, `Never`) make a feature metered; the snapshot
  value is `"<limit>/<Period>"` (still enabled for `HasFeature`).
- Counting: Redis `INCRBY` per subscriber + feature + period (in-process store without Redis). A check adds first and
  rolls back when over the limit, so racing requests cannot both take the last unit. `flush-usage-counters` (hourly :05)
  upserts `usage_records`; a counter Redis lost resumes from there.
- Fails **open** when the store is down (warning log + `metering.store.failures` metric). Like `RequireFeature`, a
  quota is only enforced with `Subscriptions:Entitlements:Enforce=true`; otherwise usage is counted and never denied.
- `GET /api/v1/subscriptions/me/usage` lists every metered feature of the plan with used / limit / next reset.
- **Who pays.** Entitlement and metering reads use `ISubscriberIdProvider.GetPayingSubscriberId()`: in multi-tenant
  mode the current tenant, for ANY member and for background work (no billing permission needed). Only managing the
  subscription (`GetCurrentSubscriberId()`: checkout, invoices, refunds) requires `tenant.billing.manage`. Before this
  split, a feature gate or quota on a member route threw 403 for every member without the billing permission. In
  single-tenant mode the payer is the signed-in user, or the `ActingUser` of background work.
- Well-known keys (`EntitlementKeys`): `bulk_import`, `bulk_export` (features) and `data_transfer_rows` (metered,
  e.g. `100000/Monthly`), used by the DataTransfer module.

### Config keys

`Subscriptions:Trials:{ReminderDaysBeforeEnd,PostTrialGraceDays,ReminderCron}`,
`Subscriptions:Dunning:{Enabled,ScheduleDays,FinalNoticeDaysBeforeGraceEnd,Cron}`,
`Subscriptions:Dunning:AutoRetry:Enabled`,
`Subscriptions:PaymentGateways:<Name>:{Mode,AllowSandboxInProduction}`,
`Subscriptions:Metering:{FlushCron,MonthlyCounterTtlDays}`.
