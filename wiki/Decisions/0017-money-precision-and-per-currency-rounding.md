# 0017. Store money as numeric(19,4) and round per ISO 4217 currency in the domain

- **Status:** Accepted
- **Date:** 2026-11-06

## Context

Every money column was `numeric(18,2)` and the code assumed two decimals everywhere: `Round(x, 2)` in tax, coupons,
credit notes and the dashboard, `PrecisionScale(18, 2)` in validators, a webhook "at most 2 decimals" check, Paymob's
`amount * 100` and `F2` formatting in emails and invoices. That is wrong for the currencies a Gulf/MENA SaaS meets:
KWD, BHD, OMR, JOD and TND have three decimals, JPY and KRW none. A KWD price of 12.345 could not be stored, a JPY
amount was shown as `1000.00`, and Paymob would have sent a wrong amount.

## Decision

- **The column is wide, the rounding is in the domain.** Every money property uses `HasMoneyPrecision()`
  (`Shared.Core`, `numeric(19,4)`): four decimals hold every ISO 4217 currency (CLF has four) without rounding on the
  way into Postgres. Percentages and tax rates are not money and keep their own precision.
- **One helper decides decimals.** `Subscriptions.Domains.Subscriptions.Billing.Currency` knows the ISO 4217
  exponent (0 / 2 / 3 / 4) and offers `Round`, `HasValidScale`, `ToMinorUnits`, `FromMinorUnits`, `SmallestUnit`
  and `Format`. Rounding stays "away from zero". A null or unknown code falls back to 2 decimals, so call sites
  that cannot name a currency behave as before.
- **Policies take an optional currency.** `TaxCalculator`, `CouponPricing`, `PricingPolicy`, `CreditNotePolicy` and
  `RefundPolicy` accept `string? currency = null` and the handlers pass the plan's or subscription's currency.
- **Validate at the edge, by currency.** Plans and coupons require a real ISO code and an amount that fits its
  decimals; `Plan` enforces it again as an invariant (create, update, schedule, force). A manual payment and a
  provider webhook amount are checked against the payment's / event's currency.
- **Providers get minor units from the same helper.** Stripe and Paymob no longer carry their own tables (Stripe
  keeps one exception: ISK and UGX are two-decimal in its API).
- **Display uses `Currency.Format`**, never `ToString("F2")`.
- **Catalog.** Product price and compare-at price get the same column type so a catalog and a plan can share a
  currency without a lossy hop. Catalog's validators still allow two decimals for now.

## Consequences

- The migration is `AlterColumn` from (18,2) to (19,4) in `subscriptions` and `catalog`. **Widening is lossless:**
  every existing value is representable and none changes. Only the `Down` migration narrows and could round.
- Old rows keep two decimals; new KWD rows store three. Nothing needs backfilling.
- `TrialReminderProcessor` still rounds its "price after trial" with the legacy two decimals for the displayed
  figure (the charge itself is computed with the subscription currency); it is a follow-up.
- Anything that sums across currencies stays out of scope: the dashboard groups per currency.

## Alternatives considered

- **`numeric(18,3)`:** fits KWD but not CLF, and still forces a migration when a four-decimal currency arrives.
- **Store minor units as `bigint`:** exact, but every query, report and DTO would convert, and the exponent would
  have to travel with each amount.
- **Per-currency column types:** not possible in one table.
