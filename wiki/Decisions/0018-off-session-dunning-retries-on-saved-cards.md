# 0018. Retry overdue renewals off-session on a saved card, idempotently, before the reminder

- **Status:** Accepted
- **Date:** 2026-11-06

## Context

Dunning (G10) only emails an overdue subscriber a fresh checkout link. Most failed renewals are soft failures that a
second charge fixes, so chasing by email loses revenue and annoys customers. Retrying needs a stored payment method
and must never charge a subscriber twice, even with overlapping job runs, crashes or a provider outage.

## Decision

- **Opt-in, off by default.** `Subscriptions:Dunning:AutoRetry:Enabled=false`. Only when it is on does checkout ask
  the provider to save the card (Stripe `setup_future_usage=off_session`, which shows the cardholder the mandate).
- **Provider references only.** `SavedPaymentMethod` stores subscriber, gateway, customer / method reference, brand,
  last4, expiry, default flag and revoked-at. Never a card number. One active default per subscriber (filtered
  unique index). The subscriber lists, switches and removes methods themselves (`.DisallowImpersonation()`,
  `.DisallowApiKey()`); removing revokes locally first, then detaches at the provider best-effort.
- **Port stays small.** `IPaymentGateway` gains `SupportsOffSessionCharges`, `ChargeSavedMethodAsync`, `DetachAsync`
  and `DescribeSavedMethodAsync`, all with "not supported" defaults, so Paymob needs no code and a new provider opts in.
  `ChargeSavedMethodAsync` returns `Succeeded | RequiresAction | Declined | Transient` and does not throw for refusals.
- **At most one charge per payment and step.** Before the provider call we save a `PaymentAttempt` (unique per payment +
  dunning step) and the `OnlinePaymentSession` the webhook will find. The call carries the idempotency key
  `dunning:{paymentId}:{step}`, the same value as the session reference. A re-run finds the attempt and does not charge.
- **Settlement is the webhook path.** A success is stored in the inbound-webhook inbox as a synthetic event and
  processed by the normal settlement code (activation / renewal, invoice, ledger). The provider's later
  `payment_intent.succeeded` (mapped only for our own off-session charges, by metadata) finds the session already
  succeeded and does nothing. If settlement fails after the money moved, the failed inbox row is retried by the
  existing job.
- **Fallback is the existing reminder.** RequiresAction, Declined or Transient (and an expired card) send the usual
  email with a fresh checkout link, the reason in the email model (`AutoRetryReason`), and an in-app notice
  (category `billing.payment-failed`). A collected payment sends no reminder.
- **Sandbox vs live.** `PaymentGatewayOptions.Mode` (`Sandbox` default | `Live`). `ProductionConfigurationGuard` refuses
  a key that contradicts the mode and, in Production, a sandbox gateway (the Fake adapter included) unless
  `AllowSandboxInProduction` is set.

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
