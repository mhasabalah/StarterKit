# 0017. Store money as numeric(19,4) and round per ISO 4217 currency in the domain

Explicit decimal precision and per-currency domain rounding preserve billing amounts across persistence and payment boundaries.

## Status

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
  way into PostgreSQL. Percentages and tax rates are not money and keep their own precision.
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
- **Catalog is not touched.** Product price columns stay `numeric(18,2)`: every catalog currency is two-decimal, and
  `catalog` is tenant data, so an `ALTER COLUMN` would rewrite the products table of every tenant for no gain. The
  `HasMoneyPrecision()` convention is for the billing money columns; a module that sells in a 3-decimal currency adopts
  it with its own migration.

## Consequences

- The migration `WidenMoneyColumnsToPrecision19x4` is an `AlterColumn` from (18,2) to (19,4) on every money column of the
  `subscriptions` schema. **Widening is lossless:** every existing value is representable and none changes.
- **Deploy cost.** Changing a `numeric` precision makes PostgreSQL rewrite each table under an `ACCESS EXCLUSIVE` lock
  for the duration of the statement. The billing tables are small next to tenant data (ledger and invoices are the
  largest), but apply it in a deploy window (`--migrate-only`), not under live traffic on a very large ledger.
- **The `Down` migration is lossy.** It narrows back to (18,2) and rounds every stored amount to 2 decimals (and fails
  for a value with more than 16 integer digits). Roll back only before a 3-decimal amount was written.
- Old rows keep two decimals; new KWD rows store three. Nothing needs backfilling.
- **Legacy rows.** A plan saved before the scale rule may carry more decimals than its currency allows (e.g. a JPY
  price of 99.5). It is not blocked: `Plan.Update` re-checks the price only when the CURRENCY changes, and a receipt
  for exactly the outstanding amount of an existing payment is accepted whatever its scale. Find and fix them with:

  ```sql
  SELECT id, currency, price, pending_price
  FROM subscriptions.plans
  WHERE price <> round(price, CASE WHEN currency IN ('BIF','CLP','DJF','GNF','ISK','JPY','KMF','KRW','PYG','RWF','UGX','VND','VUV','XAF','XOF','XPF') THEN 0
                                   WHEN currency IN ('BHD','IQD','JOD','KWD','LYD','OMR','TND') THEN 3
                                   WHEN currency IN ('CLF','UYW') THEN 4 ELSE 2 END);
  ```
- Anything that sums across currencies stays out of scope: the dashboard groups per currency.

## Alternatives considered

- **`numeric(18,3)`:** fits KWD but not CLF, and still forces a migration when a four-decimal currency arrives.
- **Store minor units as `bigint`:** exact, but every query, report and DTO would convert, and the exponent would
  have to travel with each amount.
- **Per-currency column types:** not possible in one table.

## Related

[Billing](../Subscriptions/Billing.md).
