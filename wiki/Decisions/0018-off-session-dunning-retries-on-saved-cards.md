# 0018. Retry overdue renewals off-session on a saved card, idempotently, before the reminder

Consented saved-card references support idempotent off-session renewal retries before overdue reminders.

## Status

- **Status:** Accepted
- **Date:** 2026-11-06

## Context

Dunning (G10) only emails an overdue subscriber a fresh checkout link. Most failed renewals are soft failures that a
second charge fixes, so chasing by email loses revenue and annoys customers. Retrying needs a stored payment method
and must never charge a subscriber twice, even with overlapping job runs, crashes or a provider outage.

## Decision

- **Two opt-ins, both off by default.** The operator turns `Subscriptions:Dunning:AutoRetry:Enabled` on, and the
  SUBSCRIBER ticks "keep my card" (`SaveCard` on the checkout request). Only when both hold and the gateway supports it
  does checkout ask the provider to save the card (Stripe `setup_future_usage=off_session`, which shows the cardholder
  the mandate). The row records `ConsentedAt`; dunning's own checkout links never save a card.
- **Provider references only.** `SavedPaymentMethod` stores subscriber, gateway, customer / method reference, brand,
  last4, expiry, default flag and revoked-at. Never a card number. One active default per subscriber (filtered
  unique index). The subscriber lists, switches and removes methods themselves (`.DisallowImpersonation()`,
  `.DisallowApiKey()`); removing revokes locally first, then detaches at the provider best-effort.
- **Port stays small.** `IPaymentGateway` gains `SupportsOffSessionCharges`, `ChargeSavedMethodAsync`, `DetachAsync`
  and `DescribeSavedMethodAsync`, all with "not supported" defaults, so Paymob needs no code and a new provider opts in.
  `ChargeSavedMethodAsync` returns `Succeeded | RequiresAction | Declined | Transient` and does not throw for refusals.
- **At most one charge per payment, ACROSS steps.** Before the provider call we save a `PaymentAttempt` (unique per
  payment + dunning step) and the `OnlinePaymentSession` the webhook will find. The call carries the idempotency key
  `dunning:{paymentId}:{step}`, the same value as the session reference. A re-run finds the attempt and does not charge.
  Before a NEW step charges, every earlier attempt still `Started` or `Transient` is reconciled with the provider: it is
  looked up by its reference (`GetChargeStatusAsync`), or, when no reference ever came back, replayed with its own
  idempotency key while the provider still remembers it (23 h for Stripe). A success is settled; a refusal closes it;
  a charge that may still complete, or an answer we cannot get, means NO new charge this step, and the reminder omits
  the checkout link (the subscriber could pay twice) and says a payment is being processed.
- **Settlement is the webhook path.** A success is stored in the inbound-webhook inbox as a synthetic event and
  processed by the normal settlement code (activation / renewal, invoice, ledger). The provider's later
  `payment_intent.succeeded` (mapped only for our own off-session charges, by metadata) finds the session already
  succeeded and does nothing. **If settlement is rejected after the money moved** (the payment or subscription changed in
  between), the rejection is permanent, so the inbox job will not retry it. Nothing is lost silently: the failed inbox row
  stays in `/api/v1/admin/subscriptions/webhook-events/failed`, an error is logged, and an `OffSessionChargeUnsettled`
  entry (payment id, gateway, provider reference, amount) is written to the admin audit log. A person then settles the
  payment manually or refunds the charge in the provider dashboard. There is no automatic reconciliation of that case.
- **Fallback is the existing reminder.** RequiresAction, Declined or Transient (and an expired card) send the usual
  email with a fresh checkout link, the reason in the email model (`AutoRetryReason`), and an in-app notice
  (category `billing.payment-failed`). A collected payment sends no reminder.
- **Sandbox vs live.** `PaymentGatewayOptions.Mode` (`Sandbox` default | `Live`). `ProductionConfigurationGuard` refuses
  a key that contradicts the mode (regex `(^|_)[sr]k_(test|live)_` on `ApiKey`, `(^|_)pk_(test|live)_` on `PublicKey`,
  so Stripe `sk_test_` / `rk_live_` and Paymob `egy_sk_test_` / `egy_pk_live_` all count; a test key with a live key is
  refused as mixed) and, in Production, a sandbox gateway (the Fake adapter included) unless `AllowSandboxInProduction`
  is set. **Upgrade note:** a Production deployment with a live key and no `Mode` now refuses to boot (the default is
  Sandbox): set `Subscriptions:PaymentGateways:<Name>:Mode=Live`.
- **Amounts a provider cannot take.** `IPaymentGateway.SupportsAmount` (Stripe: three-decimal currencies need a minor
  amount that is a multiple of 10; IQD/LYD/TND are not offered; HUF/TWD need whole units). Checkout answers 409
  `amount_not_supported`; dunning and the automatic charge skip such a gateway and fall back to the next one or the link.

## Consequences

- A flaky provider cannot cause a double charge; the cost is one extra table row per attempted step.
- 3-D Secure cards still need the cardholder: they fall back to the link, with the reason spelled out.
- The saved card is captured after the settlement commits; if capture fails the payment is unaffected and the next
  cycle is chased by email.
- Card data lives at the provider, so PCI scope does not grow.

## Alternatives considered

- **Provider-side subscriptions (Stripe Billing):** moves invoicing, proration and tax out of our ledger.
- **Charging inside the settlement transaction:** holds a database transaction open across a network call.
- **Retrying every dunning email run without an attempt row:** relies on the provider alone for idempotency and gives
  no audit trail of what was tried.

## Related

[Billing](../Subscriptions/Billing.md), [Online Payments](../Subscriptions/Online-Payments.md).
