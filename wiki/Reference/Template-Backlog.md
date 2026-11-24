# StarterKit — Template Backlog

This backlog tracks completed and planned changes to the StarterKit template.

Created when StarterKit was extracted from
BateCom (2026-11-01). Tick items off (or delete them) as they are handled.

> Full review with every finding and the wave plan: Obsidian `projects/starterkit/2026-11-01 StarterKit Full Review.md`.
> Waves: 0 BateCom hotfix (owner handles) → **1 security ✅** → 2 money correctness → 3 reliability →
> 4 admin-portal P0 → 5 tests/quality → 6 portal P1/P2.

## Wave 6 — SaaS gap analysis (2026-11-04)

Senior gap review of features a production SaaS needs. ✅ = shipped (wave 6, PR #11), 🚧 = in progress, ⬜ = open.

> **Open work lives in [GitHub Issues](https://github.com/mhasabalah/StarterKit/issues)** (labels `security`, `ops`,
> `owner-action`, `enhancement`, `dependencies`). This page is the history of what each wave shipped and why; an open
> item here links to its issue, and closing the issue (`Fixes #N` in the PR) is recorded in the next wave summary.

Follow-up (done, ADR [0017](../Decisions/0017-money-precision-and-per-currency-rounding.md)): billing money columns are numeric(19,4) and rounded per ISO 4217 currency.

**P0 — before the first real customer**

| # | Item | Status |
|---|---|---|
| G1 | Real payment adapters (**Stripe + Paymob**) behind `IPaymentGateway`, `RefundAsync`, refund webhooks posting to the ledger | ✅ |
| G2 | Real malware scanner adapter (ClamAV) replacing `NoOpMalwareScanner` ([Security](../Attachment/Security.md#malware-scanning)) | ✅ |
| G3 | Analyzers + `TreatWarningsAsErrors` (= #8) | ✅ |
| G4 | Identity + Attachment module tests (= #10) | ✅ |
| G5 | Data Protection keys: KMS option (Azure Key Vault, AWS KMS); certificate option already exists | ✅ |

**P1 — expected by most SaaS products**

| # | Item | Status |
|---|---|---|
| G6 | Session/device management: list my sessions, revoke one, revoke others; admin view; `sid` claim | ✅ |
| G7 | Outbound customer webhooks (new `Webhooks` module): signed (Standard Webhooks), retries, delivery log, SSRF guard | ✅ |
| G8 | API keys / personal access tokens, tenant-scoped, permission scopes | ✅ |
| G9 | Usage metering + quotas per plan (`IUsageMeter`, `.RequireQuota`) | ✅ |
| G10 | Coupons / promo codes, trials, dunning (notice cadence) | ✅ |
| G11 | Generic business-entity audit trail (`IAuditable`, same-transaction `audit_entries`) | ✅ |
| G12 | Runtime feature flags (per tenant/user, % rollout, kill switch, `.RequireFlag`) | ✅ |
| G13 | In-app notification inbox + real-time push (SignalR, Redis backplane, push-only hub) | ✅ |
| G14 | Bot protection (CAPTCHA/Turnstile) on register, login, forgot-password, OTP | ✅ |

**P2 — market-dependent**

| # | Item | Status |
|---|---|---|
| G15 | Enterprise SSO per tenant (SAML/OIDC) + SCIM provisioning ([SSO](../Identity/Enterprise-SSO.md), [SCIM](../Identity/SCIM.md)) | ✅ |
| G16 | Passkeys (WebAuthn) ([Passkeys](../Identity/Passkeys.md)) | ✅ |
| G17 | Full-text search abstraction: the Search module behind an engine port, PostgreSQL first and Meilisearch, with facets, admin API and zero-result analytics ([Search](../Modules/Search.md), [ADR 0036](../Decisions/0036-search-module-and-engine-port.md)) | ✅ |
| G18 | Mobile push (FCM for Android, iOS via APNs, Web) channel + session-bound device-token registration | ✅ |
| G19 | Bulk CSV/Excel import/export jobs with progress and error report ([DataTransfer](../Modules/DataTransfer.md)) | ✅ |
| G20 | Blobs in tenant data export + zero-downtime tenant move (= #14 remaining) | ⬜ [#54](https://github.com/mhasabalah/StarterKit/issues/54) |
| G21 | Durable saga repository (= #27) ([ADR 0023](../Decisions/0023-durable-sagas-on-module-dbcontexts.md)) | ✅ |
| G22 | Load/performance tests (k6) with a CI baseline (HTTP scenarios: `tests/load`, `load-test.yml`; DataTransfer engine baseline: `datatransfer-perf.yml`) | ✅ |
| G23 | `dotnet new` project + module templates (= #12, #13) ([Templates](../Getting-Started/Templates.md), ADRs [0019](../Decisions/0019-module-manifests-and-discovery.md) / [0020](../Decisions/0020-dotnet-new-templates.md)) | ✅ |
| G24 | Observability-lite compose profile (= #21) ([Lite profile](../deployment/Observability.md#lite-profile-prometheus--grafana-only)) | ✅ |

**High priority — documentation and brand**

| # | Item | Status |
|---|---|---|
| G25 | Public documentation site for the template: senior-level, built from the `wiki/` Markdown, hosted on GitHub, with a modern light and dark design and a brand identity | ✅ |

G25 scope (requested 2026-11-08, completed in Wave 15):
- **Source stays Markdown.** `wiki/` remains the single source of truth; the site is generated HTML, published by CI on
  GitHub (GitHub Pages), so docs and code change in the same PR.
- **Senior-level structure.** Reorganized for readers rather than mirroring the folders: getting started, concepts and
  architecture, how-to guides per task, reference (template switches, configuration, guardrails), the ADRs, and
  deployment. Navigation, full-text search, a page outline, deep links, and link checking in CI.
- **Modern design, light and dark mode.** Follows the system setting with a manual toggle, readable code blocks, works
  on phones.
- **Brand identity for the template.** Name treatment, logo, color palette and typography, used on the site and on
  where the template is presented (the repository README and social preview, the `StarterKit.Templates` package page).
- **Publication:** Astro Starlight, template-only; the site is excluded from generated products and the template pack.
  Repository visibility and any custom domain remain owner choices; GitHub Pages on a private repository needs a
  paid plan.

Wave 15 (G25) (2026-11-10, branch `docs/g25-documentation-site`): **documentation site and brand.**
- **Shipped:** reader-first Astro Starlight navigation in `docs-site/`, synced from the canonical `wiki/`; rewritten
  references, light/dark brand assets, search, Mermaid rendering and link/contrast checks.
- **Publication:** `.github/workflows/docs.yml` builds PR artifacts and publishes master to GitHub Pages; the site
  stays out of generated products and the template pack ([ADR 0024](../Decisions/0024-documentation-site.md)).
- **Owner follow-ups:**
  - ~~Enable GitHub Pages with source "GitHub Actions"~~ Done 2026-11-11: the site is live, linked from the README
    and the repository About.
  - Upload `docs-site/public/og-image.png` as the repository social preview: [#48](https://github.com/mhasabalah/StarterKit/issues/48).

Wave 16 (2026-11-11/12, PRs #40, #41/#42, #43, #44): **security bump and the defects the G25 review surfaced.**
Writing the wiki against the code found places where the code did not do what the docs or CLAUDE.md promised.
- **Security (#40):** CVE-2026-114147 (ASP.NET Core DoS, `SignalR.StackExchangeRedis` ≤ 10.0.14) and CVE-2026-120006
  (Aspire elevation of privilege, < 13.6.5) made `NuGetAudit` fail restore on every branch. The 10.0.14 release train
  moved to 10.0.15, Aspire packages and `Aspire.AppHost.Sdk` to 13.6.5, and `dotnet-ef` to 10.0.15.
- **Backups (#42):** `archive_command` called pgBackRest, which `postgres:16-alpine` does not include. Every push
  failed, WAL piled up until the disk would fill, and no backup had its WAL.
  - `infra/postgres` (PostgreSQL 16 + pgBackRest 2.57) now runs both `postgres` and `pgbackrest`, with
    `pgbackrest-archive-wal` and `init: true`.
  - `restore.sh` validates the request before stopping anything and runs the migrator before restarting.
  - Verified end to end, including point-in-time recovery ([Backups](../deployment/Backups.md)).
- **Network (#42):** the production compose published PostgreSQL, Redis, Seq and the API on 0.0.0.0, and Docker
  bypasses UFW. They now bind to `${BIND_ADDRESS:-127.0.0.1}` ([Docker Compose](../deployment/Docker-Compose.md)).
- **Rollback (#42):**
  - `rollback.sh` set the wrong variable and restarted the same image.
  - `rollback.yml` piped a dump into the live database without `ON_ERROR_STOP`: it exited 0 and changed nothing.
  - It now takes `image_tag` and/or `restore_database`, restores with drop + recreate in one transaction, then
    runs the migrator ([CI/CD](../deployment/CI-CD.md#rollback-behavior)).
- **Production boot (#42):** the `CHANGE_ME` defaults of APNs and the Stripe/Paymob gateways made
  `ProductionConfigurationGuard` refuse to boot a default production deploy. It also broke the scheduled k6 load
  test since 2026-11-06.
  - Optional, disabled secrets default to `""`. `ProductionConfigurationGuardTests` pin both rules against the
    shipped settings ([Configuration](Configuration.md)).
- **Smaller (#42):**
  - The Hangfire dashboard secret mapped to the wrong section.
  - The SSO kill switch is now re-checked on the callback and on the code exchange.
  - The tenant unique-index guardrail asserts `TenantId` comes **first**.
  - New `EnumStorageTests`: nine legacy integer enum columns are listed, and new ones fail the build.
- **Docs site (#43, #44):**
  - The deploy job runs on manual runs from `master` too.
  - The build job got `pages: read`; Configure Pages had failed with "Resource not accessible by integration".
  - The README links the site.
- **Open from this wave** (tracked as issues):
  - [#46](https://github.com/mhasabalah/StarterKit/issues/46) Register the self-hosted deploy runner (owner).
  - [#47](https://github.com/mhasabalah/StarterKit/issues/47) First production deploy with #42: `git pull`, `init-pgbackrest.sh`, SSH tunnel (owner).
  - [#49](https://github.com/mhasabalah/StarterKit/issues/49) Enable a Content-Security-Policy in the production Nginx config.
  - [#50](https://github.com/mhasabalah/StarterKit/issues/50) Guardrail for tenant-first order of raw-SQL unique indexes.
  - [#51](https://github.com/mhasabalah/StarterKit/issues/51) Migrate the nine legacy integer enum columns to strings (optional, by decision).
  - [#52](https://github.com/mhasabalah/StarterKit/issues/52) Dependabot: ignore .NET major bumps of the Docker images (#37/#38).

Wave 14 (2026-11-08, branch `chore/backlog-leftovers`): **the smaller leftovers of the older sections.**
- **Moderation:** approve, reject, request changes, publish and the pending queue need `catalog.moderate` instead of
  `AdminOnly` (same callers; API keys need the scope) ([Catalog](../Modules/Catalog.md)).
- **Billing dashboard cache:** invalidated after every committed ledger entry, payment review change and subscription
  status change, using a generation token in Redis. `ILedgerRepository.Add` stages `LedgerEntryRecordedEvent`. A
  cache failure no longer fails the request ([Billing](../Subscriptions/Billing.md)).
- **Saga timeouts:** a message scheduler on every transport (SQL scheduler on PostgreSql, delayed on InMemory, on
  RabbitMQ with the delayed exchange). Pinned by `DurableSagaTests` ([Messaging](../Architecture/Messaging.md)).
- **dotnet-ef:** a local tool at the EF Core version (`.config/dotnet-tools.json`, `dotnet tool restore`), guarded by
  `ToolManifestTests`.
- **Aspire:** `MultiTenancy:DedicatedSample=true` adds a dedicated tenant database ([Aspire](../Infrastructure/Aspire.md)).
- **Decided, no code:**
  - No per-tenant storage containers by default ([Storage](../Attachment/Storage-Providers.md)).
  - The per-product items (#15-17, #20) moved to the
    [New-Project checklist](../Getting-Started/New-Project.md#product-decisions-before-the-first-production-migration).
- **Stale entries closed:**
  - TOTP replay is already guarded by `TotpReplayGuard`.
  - The Data Protection KMS option shipped with G5.
  - ImageSharp was replaced by SkiaSharp (2b).
  - #10 shipped with G4.
- **Still open:** HealthChecks 10.x (not released, checked 2026-11-12): [#55](https://github.com/mhasabalah/StarterKit/issues/55).

Wave 13 (2026-11-08): **G21 durable sagas + G24 observability-lite.**
- **G21** ([ADR 0023](../Decisions/0023-durable-sagas-on-module-dbcontexts.md)):
  - Sagas are no longer scanned onto the in-memory repository.
  - A module registers each saga in its new `ConfigureMessaging` with `AddDurableSagaStateMachine<TMachine, TState,
    TDbContext>()`: MassTransit's EF Core repository on the module's DbContext, pessimistic locking, and the in-memory
    outbox on the saga endpoint.
  - The state is mapped with `SagaStateConfiguration<T>`.
  - Guarded by `SagaArchitectureTests`.
  - Pinned by `DurableSagaTests` on both transports: restart, 25 concurrent events, tenant isolation, no publish on a
    failed save. Each was mutation-checked: an in-memory repository, a removed outbox and optimistic mode each fail them.
  - ~~Follow-up: a message scheduler for saga timeouts~~ Done in wave 14 (every transport).
- **G24** ([Lite profile](../deployment/Observability.md#lite-profile-prometheus--grafana-only)):
  - `docker-compose.observability-lite.yml` (+ the dev override and the worker companion): Prometheus scrapes the API
    and worker `/metrics` through Docker DNS, and Grafana gets the Prometheus datasource only.
  - The alert rules are the same as the full stack's.
  - The OTLP endpoint is cleared, since there is no collector. Logs and traces stay in Seq.
  - About 0.75 GB versus about 5 GB. `make obs-lite-up` / `obs-lite-down`.

Wave 12 (2026-11-08): **G23 `dotnet new` templates**
([Templates](../Getting-Started/Templates.md), [ADR 0019](../Decisions/0019-module-manifests-and-discovery.md),
[ADR 0020](../Decisions/0020-dotnet-new-templates.md)).
- **Module manifests and discovery.**
  - Every module has an `<M>ModuleManifest`. `Api.csproj` references modules by glob, and `AddModules()` /
    `UseModules()` replace the hand-kept lists in `Program.cs`.
  - The startup order is `ModuleOrder` data, guarded by `ModuleDiscoveryTests`.
  - The Dockerfile restore layer copies every csproj automatically.
  - A new module now needs no host edits.
- **Product template** (`dotnet new starterkit`): the repository is the template.
  - Switches: `--sample`, `--multitenancy`, `--transport`, `--storage`, `--ai-tooling`, `--ci`, `--deploy`,
    `--github-owner`.
  - The name is renamed in all three spellings, and the GUIDs are regenerated.
- **Module template** (`dotnet new starterkit-module`):
  - four projects plus a manifest
  - an example aggregate with CRUD, optimistic concurrency, tenant isolation, unit and Testcontainers tests, and RLS
  - `--data-plane`, `--outbox` and `--example`
  - one `dotnet ef`-generated migration set per option combination
- **Pack and CI.** `StarterKit.Templates` (`templates/`). `templates.yml` generates four products and seven modules,
  each of which must build and pass the architecture, module and E2E tests. A `templates-v*` tag publishes to GitHub
  Packages.
- **Found on the way:**
  - A module without endpoints made Carter scan every assembly and register all endpoints twice (startup crash).
  - `"starterkit"` connection-string literals in every module are now `DatabaseDefaults.ConnectionStringName`.
  - Webhooks and API-key tests no longer need the Catalog sample.
  - The single-tenant E2E factory pins `MultiTenancy:Enabled=false`.

Wave 11 (2026-11-07, PRs #17-#23): **follow-ups of the shipped work + G22.**

Six workstreams were each implemented and then reviewed by an architecture audit, with 99 findings fixed.
- **Billing** (#21): numeric(19,4) money with per-currency rounding ([ADR 0017](../Decisions/0017-money-precision-and-per-currency-rounding.md)), saved cards with an
  opt-in, off-session dunning retry on Stripe ([ADR 0018](../Decisions/0018-off-session-dunning-retries-on-saved-cards.md)), sandbox/live gateway mode.
- **SSO/SCIM** (#17): SAML metadata refresh, certificate expiry alerts, the SCIM enterprise extension, a multi-tenant SSO E2E.
- **Notifications** (#18, #22): per-group mutes and quiet hours, a direct APNs adapter, in-app/push on business
  events, and closing hub sockets when a session is revoked.
- **DataTransfer** (#20): the `catalog.categories` and `tenancy.members` definitions.
- **G22** (#19): k6 HTTP load tests with a nightly CI baseline ([Load-Testing](../Testing/Load-Testing.md)).
- **CI date bomb** (#23): the DataTransfer integration-test clock was pinned to 2026-11-06, so every branch failed
  from the next day on. Never pin a fixture clock to a date when a test compares it with PostgreSQL `now()`.

**Hints before the next production deploy:**
- **Gateway mode.** An enabled payment gateway with a live key and no `Mode` now refuses to boot. Set
  `Subscriptions:PaymentGateways:<Name>:Mode=Live` ([Billing](../Subscriptions/Billing.md)).
- **Money migration.** `WidenMoneyColumnsToPrecision19x4` rewrites the Subscriptions tables under an ACCESS
  EXCLUSIVE lock. On a large database, run `--migrate-only` in a maintenance window. Its `Down` narrows the columns
  again and loses digits.
- **Stripe placeholder.** It is renamed to `CHANGE_ME_stripe_secret_api_key`. Check that secrets and env files
  override `ApiKey`.
- **Category permission (breaking).** Category CRUD now needs `catalog.categories.manage`. In multi-tenant mode, a
  platform Admin without a membership loses it. Grant the permission to custom tenant roles
  ([Catalog](../Modules/Catalog.md)).
- **APNs key.** It is read only from `Push:Apns:PrivateKeyFile` (Docker secret `apns.p8`). The earlier
  `Push:Apns:PrivateKey` setting and the `apns_private_key` secret are gone. A sandbox/production mismatch gives
  `BadDeviceToken`, which is logged but no longer deletes devices.
- **Forced email.** A forced email (`SendAsync(NotificationChannel.Email)`) is muted when its category's group allows
  email mutes. Uncategorized, `security` and `billing` email never are.
- **DataTransfer category.** It is renamed to `data-transfer.job-finished`. Clients that filter on the old
  `datatransfer.` prefix need an update.
- **Time zones.** Linux images need `tzdata` for quiet hours. The `aspnet:10.0` Debian base image includes it.

**Still open from this wave:**
- [ ] **Live Stripe test.** Off-session charge, reconcile, describe and detach are tested only against a stub HTTP
  endpoint. Run one real charge against a Stripe test account before enabling `Dunning:AutoRetry`.
- [ ] **Paymob saved cards.** Paymob implements only the port defaults, so its dunning falls back to the email link.
  Add the saved-card token flow (TOKEN callback + MOTO integration id) when a merchant account is available.
- [x] **Flaky E2E.** `PendingAttachmentFlowE2ETests.Anonymous_upload_then_sign_in_associate_and_download` failed once
  in CI with 409 "modified by another request". Fixed in PR #25: associate re-reads once when only a thumbnail
  appeared (`Integration/PendingAssociateRaceTests`).
- [x] **Unsigned SAML metadata.** Done (2026-11-08): per-connection `samlRequireSignedMetadata` and pinned
  `samlMetadataSigningCertificates`, default off ([Enterprise SSO](../Identity/Enterprise-SSO.md)).
- [ ] **Hub fan-out.** Closing sockets across replicas is tested with a Redis round trip, not a live two-replica
  deployment. Verify on the first multi-replica environment.
- [ ] **Member import at scale.** Invitations save one row at a time, and a crash between the save and the email
  leaves an unsent invitation, which admins resend ([DataTransfer](../Modules/DataTransfer.md)).
- [ ] **Load-test budgets.** The p95 budgets are wide, with CI multiplying them by 3 for the 2-CPU runner. Tighten
  them once a week of nightly numbers exists.
- [ ] **Housekeeping.** Delete the merged remote branches of PRs #16-#23.

Wave 10 (2026-11-06, [ADR 0016](../Decisions/0016-bulk-data-transfer-on-hangfire.md)): **G19 bulk import/export.**
- New `DataTransfer` module (engine) + `catalog.products` definitions (reference); owning modules plug in through
  `DataTransfer.Contracts`.
- Jobs on their own Hangfire queue `data-transfer`; `Hangfire:Servers:<Name>:Enabled` + `docker-compose.worker.yml`
  run the same image as worker replicas.
- Lease + per-batch checkpoint + own retry + stall sweep; dry run, `BestEffort` / `ValidateFirst`, error report, cancel,
  7-day retention; Sylvan streaming readers, own CSV/XLSX writers with the formula-injection guard and zip-bomb limits.
- Attachment: `EntityType.DataTransferJob` and the trusted `OpenAttachmentContentQuery`.
- Found by the E2E tests: a multipart form leaves missing enum fields at `0` (property initializers are not applied),
  so the request fields are nullable with defaults in the endpoint.
- `/starterkit:review` of the wave (15 issues, all fixed):
  - Security: jobs now re-check the requester's membership and run in their branch scope. Before this fix, a branch
    manager's export saw every branch; a multi-tenant E2E now guards it. Auditors need the definition permission
    for files.
  - `datatransfer-queue` health check.
  - Retention purge in keyset batches (it stopped at 100 per night).
  - Localized engine messages (en/ar).
  - Idempotent cancel (the version made a running cancel 409).
  - One file parse fewer.
  - Tenant-first `(tenant_id, created_at, id)` products index for exports.
  - Export size checked before the upload; module 4xx errors no longer retried.
  - Tests: Excel-authored workbooks (shared strings, numeric codes, dates, 3 server cultures), lease loss, shutdown,
    error cap, server roles, validators in en/ar.
- Senior follow-ups, done in the same wave:
  - **Live progress:** `datatransfer.job-progress` user events over the notification hub (new generic
    `IUserEventPublisher` / `UserEvent` hub method), throttled.
  - **Plans:** `bulk_import` / `bulk_export` features and the `data_transfer_rows` metered quota, counted per batch.
  - **Fix found on the way:** feature gates and quotas threw 403 for every tenant member without
    `tenant.billing.manage`. New `ISubscriberIdProvider.GetPayingSubscriberId()` for plan reads.
  - **`ActingUser`** scope for background work, so `CreatedBy` and the audit trail name the requester.
  - **Typed XLSX cells:** `TransferColumn.Kind` (Number, Date), bold frozen header, column widths.
  - **G22 slice:** the opt-in `PerformanceBaselineTests` (100k rows: about 64k rows/s import, 93k rows/s export) and
    the nightly `datatransfer-perf.yml`.
- Follow-ups: ~~more definitions (categories, members)~~ done (`catalog.categories`, `tenancy.members`; new tenant permission `catalog.categories.manage`; category endpoints now use it instead of `AdminOnly`); users per project; full G22 HTTP load tests (k6/NBomber).

Wave 9 (2026-11-06, [ADR 0015](../Decisions/0015-malware-scanning-clamd-fail-closed.md)): **all P0s are done.**

**G2: ClamAV malware scanning.**
- Own clamd `INSTREAM` client (`ClamAvMalwareScanner`, no new package), selected with `Attachment:Validation:Scanner:Provider=ClamAv`.
- Fails closed: 503 `malware_scanner_unavailable`, and 400 `malware_detected` for an infected file.
- `FailOpen` is an explicit opt-out.
- The `clamav` health check reports Degraded on failure, never unready.
- `attachment.malware.scans` metric.
- Startup warns when scanning is off; `RequireVirusScanning` makes that fatal.
- clamd runs with `AlertExceedsMax yes`: without it, content clamd skipped (too big) is reported clean. The real-image test proves it.
- `docker-compose.clamav.yml` and the Aspire switch `Attachment:ClamAv`.
- Tests: in-process fake clamd; opt-in real-image test (`STARTERKIT_CLAMAV_TESTS=1`); HTTP E2E.

**G4: the HTTP tests that were still open.**
- Refresh rotation, and reuse revoking the family.
- Logout and logout-all.
- Change-password session kill.
- Password lockout.
- Forgot → reset → login by email link and by phone OTP.
- Register → confirm → login.
- Pending upload → associate → download, plus session ownership.
- Unit tests for the onboarding verify-phone and resend handlers.

**Bugs these tests found, now fixed:**
- **Logout left the session alive.** It revoked only the presented tokens, so other access tokens of the same session kept working until they expired. Logout now ends the `sid` session through `ISessionManager`.
- **Two avatars cancelled each other.** Associating two avatars at once made each delete the other, leaving the user with none. A new avatar now replaces only older ones.
- **Windows file lock on delete.** Local storage opened reads without `FileShare.Delete`, so on Windows a delete failed with 500 while the thumbnail job was reading the file.
- **Flaky E2E mail lookup.** It scanned Hangfire one state at a time and could miss or double-count a job. It now reads `hangfire.job` in one query.

**Follow-ups of the review, done in the same PR:**
- **Pending delete vs thumbnail race.** The thumbnail job could save the row mid-delete; the user got 409 and the
  row survived its deleted file. The delete now re-reads once and completes, including the new thumbnail
  (`Integration/PendingDeleteRaceTests`, real PostgreSQL conflict).
- **Lockout no longer enumerates accounts.** A locked-out account gets the same 401 `Invalid credentials` as an
  unknown one.
- **ClamAV in CI.** `clamav-live.yml` runs the real-image test nightly and on PRs that touch the scanner.
- **Scanner timeout on `TimeProvider`.** The startup check exempts only `Development` and `Testing`, like Identity.
- **Verified live.** The real API with real clamd: EICAR gets 400, a clean file gets 200; clamd stopped gives 503 and
  `/health` reports `Degraded` (200). The compose overlay's own `clamav` service comes up healthy with
  `AlertExceedsMax yes`.

Follow-ups known up front, now done (ADR [0018](../Decisions/0018-off-session-dunning-retries-on-saved-cards.md)): automatic
off-session card retry in dunning on saved payment methods; sandbox vs live gateway mode and sandbox keys per project.

Wave 8 (2026-11-05, ADRs [0013](../Decisions/0013-enterprise-sso-and-scim.md) / [0014](../Decisions/0014-passkeys-on-built-in-identity.md)):
G15 + G16 shipped, plus the email design system v2 ([page](../Modules/Notification-Email-Design-System.md)), the
email OTP channel (`OneTimeCode`, closes the G13/G18 follow-up below) and the [feature catalog](Features.md). G4
progress: E2E for passkeys, SSO and SCIM. Follow-ups, done: SAML metadata-URL refresh job with certificate expiry
alerts, multi-tenant E2E of SSO (`SsoTenantTests`) and SCIM enterprise-extension attributes with `invalidPath`
([SSO](../Identity/Enterprise-SSO.md), [SCIM](../Identity/SCIM.md)). Still open from G4: refresh/logout/password-reset
E2E and the attachment pending flow over HTTP. The architecture review items of 2026-11-05 are closed
(discovery-cache eviction across replicas with the old authority, tenant-role validation for SSO default roles and
SCIM group mappings, SCIM directory repository and single-query counts, one `AddDnsTxtVerifier()`, en/ar messages
and request-culture security alerts, enforcement lapsing with the plan, and the missing tests incl. a SAML E2E).
By design and documented: a missing `email_verified` claim is accepted on a DNS-verified domain (Entra never sends
it); SCIM deactivation revokes all sessions of the account.

Follow-ups of G13/G18 (2026-11-05, [ADR 0012](../Decisions/0012-realtime-inbox-and-push.md)): ~~per-category mute and
quiet hours~~ (done: per-group mutes and quiet hours, see
[Notification-Realtime-and-Push](../Modules/Notification-Realtime-and-Push.md#mutes-and-quiet-hours)); ~~a direct APNs
adapter behind `IPushProvider`~~ (done: `Push:Apns`); ~~push and in-app payloads on the existing business events~~
(done: billing, membership, webhooks, catalog, data transfer; categories `<group>.<event>`, see
[Notification-Realtime-and-Push](../Modules/Notification-Realtime-and-Push.md#which-events-notify)); an OTP email
template so `VerificationPurpose.EmailVerification` stops returning `ChannelUnavailable`; ~~drop the hub socket
immediately on session revocation~~ (done: `UserSessionsRevokedIntegrationEvent`, see
[Closing sockets on revocation](../Modules/Notification-Realtime-and-Push.md#closing-sockets-on-revocation)).

## 2026-11-04 — Production-readiness wave

Implemented:
- Deploy-time migrator (`Api.dll --migrate-only`) under a PostgreSQL advisory lock, plus `Database:MigrateOnStartup`
  (off in `appsettings.Docker.json`). The migration connection string is a Docker secret.
- Production guard against the `InMemory` messaging transport.
- Request timeouts (`RequestTimeouts:DefaultSeconds` / `LongRunningSeconds`, 504 on expiry).
- Health wiring: `/health` readiness gates deploys, `/alive` liveness backs the container healthcheck (#19).
- CI variables fail fast when missing (#18).
- RLS bypass by role (`RlsBypass=Role`, `tenant_rls_bypass`), PgBouncer pooling warning, and a startup check of the
  runtime role.
- Redis-based L1 cache invalidation broadcast across instances (#26).
- Online payment gateway port with a webhook inbox.
- `Idempotency-Key` filter for unsafe endpoints.
- Entitlements: `.RequireFeature(...)` on endpoints.
- Self-service account deletion and data export, with `UserDeletedIntegrationEvent` fan-out.
- JWT signing key rotation.
- Outbox metrics, dead-letter admin endpoints and Prometheus alerts, including "outbox metrics missing" (#28).

Marked done: #18, #19, #26, #28.

Still open:
- ✅ Licensing decisions made 2026-11-04 (see "Licensing decisions" and [Third-Party-Licenses](Third-Party-Licenses.md)).
- A real payment provider adapter per project (the template ships only the port and the inbox).
- Refund webhook posting to the ledger.

## Wave 5 — Identity hardening + platform correctness (2026-11-03) — Catalog items deferred by owner

- **Email confirmation:**
  - `POST /api/auth/email/send-confirmation` (the caller only) and `POST /api/auth/email/confirm`.
  - The link is sent automatically on email registration and when a phone account adds an email.
  - Tokens come from their own provider with a 24h lifetime, bound to the security stamp.
  - Forgot-password by email works again for confirmed users.
  - Fixed: phone accounts (placeholder email) could never add a real email in complete-profile.
- **One OTP system (#22):** Identity `IOtpCodeService` (PostgreSQL, HMAC, atomic attempts and cooldown); Notification only delivers the code. The Redis verification store and the `notification_verification_hmac_key` secret are removed.
- **TOTP replay guard:** an accepted code is consumed for 180s (Redis `SET NX`, or a distributed-cache fallback). Recovery codes are confirmed single-use under concurrency.
- **Data Protection keys:**
  - Encrypted at rest with a certificate (thumbprint, or a PFX plus a secret password).
  - Shared `ApplicationName`.
  - Startup warning when unprotected; set `RequireKeyEncryption=true` to fail instead. Overlay: `docker-compose.dataprotection.yml`.
- **Role changes (#23):** every role grant or revoke ends all of the target's sessions, so permissions apply immediately.
- **Timestamps (#6/#7/#24):**
  - The legacy Npgsql switch is removed; everything is timestamptz/UTC. A new Identity migration, `UseTimestampTz`, converts the columns.
  - Client date filters are normalized through `UtcDateTimeExtensions.AsUtc()`.
  - The `PendingModelChangesWarning` suppression is removed. The new `PendingModelChangesTests` guard runs as its own CI step.
- **Dockerfile (#25):** the restore layer copies the full project closure. The ImageSharp license key comes in as a BuildKit secret (see 2b).
- **E2E stability:** the E2E host raises the `auth`/`otp` rate limits (all clients share one IP), so the suite is no longer order-dependent.

### Follow-ups / open after Wave 5
- ~~**Catalog moderation → `catalog.moderate`**~~ Done in wave 14.
- ~~**ImageSharp:** license key~~ Moot: ImageSharp was replaced by SkiaSharp (2b), no key needed.
- **Data Protection rollout:** setting `ApplicationName` on a live deployment orphans existing encrypted authenticator keys. Set it to the old value (derived from the content root, `/app`), or have users re-enroll 2FA. (The KMS key-protection option shipped with G5.)
- **OTP behaviour changes:** a reused onboarding code now says "Invalid verification code." The standalone `PhoneVerification` purpose shares its code and cooldown with `/api/identity/phone/*`.
- Tenant roles aren't in the token: other instances see changes after `MultiTenancy:Cache:LocalSeconds` (see #26).
- ~~Update the `dotnet-ef` tool to 10.x~~ Done in wave 14: a local tool pinned to the EF Core version.
- Some E2E tests read Hangfire storage through DI, because the eager `ConnectionStrings__starterkit` env var is process-global. A second factory in the same process could point Hangfire at another database; keep one E2E host per process.

## Wave 4 — Admin portal P0 (2026-11-02)

- **Permissions** (`Shared.Core/Authorization/Permissions.cs`, `.RequirePermission(...)`):
  - New roles Finance (`66666666-…`) and Support (`77777777-…`).
  - Static `RolePermissionMap` (now in `Shared.Core`); permissions derived per request from role claims by
    `PermissionClaimsTransformation` (no longer in the JWT); one `perm:*` policy per permission.
  - Every billing and Identity admin endpoint has moved to permissions.
  - Granting or revoking staff roles is SuperAdmin-only.
- **Admin 2FA (TOTP)** on ASP.NET Identity's authenticator:
  - Encrypted key and hashed single-use recovery codes.
  - Data Protection keys persisted in the DB.
  - Challenge token, then `POST /api/auth/2fa/verify`.
  - Enforced for SuperAdmin/Admin/Finance: `Enforce` in production, `Warn` in Development/Testing; 403 `mfa_required`.
  - External login no longer bypasses 2FA. SuperAdmin can reset a user's 2FA.
- **Identity admin audit:** append-only, written in the same transaction, no secrets; `GET /api/v1/admin/identity/audit`.
- **Billing console** (`wiki/Subscriptions/Billing.md`):
  - Admin subscription search (SQL-side) and Subscriber-360.
  - **Immutable ledger** (`ledger_entries`, append-only enforced in code).
  - **Billing dashboard** (MRR/ARR per currency, revenue/net from the ledger, churn, queue age).
  - Enriched pending-proof queue with filters.
  - **Manual and partial payments** (`PartiallyPaid` + receipts).
  - Extend, reactivate and comp.
  - Duplicate-proof detection (SHA-256).
  - Admin notification routing + daily digest job.
  - One `PaymentSettlementService` completion path.
- **Architecture test** `AdminEndpointAuthorizationTests`: every `/admin` and `/superadmin` endpoint needs a role or permission policy, and every `perm:*` policy used must be registered.
- **Integration:**
  - Identity and Subscriptions migrations regenerated.
  - Removed the E2E `PendingModelChangesBootstrapper` stop-gap, so forgotten migrations fail E2E again.
  - Finance views proofs through `/admin/payments/{id}/proof` (gated by `billing.read`), not the generic attachment download.
- **Tests:** **1,682 green across 12 projects** (incl. real password + TOTP login E2E, Finance/Support permission E2E).

### Follow-ups created by Wave 4
- **Rollout:** production enforces admin 2FA. Set `Identity:TwoFactor:EnforcementMode=Warn` until admins have enrolled.
- **Breaking:** login returns `requiresTwoFactor` + `twoFactorChallengeToken` for 2FA users (no tokens until `/2fa/verify`).
- ~~A TOTP code can be replayed within its window~~ Done in Wave 5: `TotpReplayGuard` remembers used codes (Redis).
- ~~Data Protection keys are stored unencrypted~~ Done: certificate or KMS key protection (G5).
- ~~Catalog moderation endpoints are still `AdminOnly`~~ Done in wave 14 (`catalog.moderate`).
- ~~Billing dashboard cache has no invalidation~~ Done in wave 14 (generation token, invalidated on money events).

## Wave 3 — Reliability (2026-11-02)

- **Domain events:** only *committed* domain events are dispatched, and failed or rolled-back saves discard them.
  - Every background job uses `IUnitOfWorkRunner` / `IDomainEventDispatcher`, enforced by
    `Api.Tests/Architecture/BackgroundJobEventDispatchTests` (IL scan).
  - Job-raised events now fire (e.g. SubscriptionExpired email).
- **Concurrency** is translated in one place (the mediator behavior). Jobs skip a conflicting item with
  `IsConcurrencyConflict()` instead of aborting the batch.
- **TimeProvider** is used by the interceptor and jobs. Cache helpers take a `CancellationToken`.
- **Attachment:**
  - Two-phase upload (Uploading → Ready) and a visibility filter.
  - Async blob deletion job with backoff.
  - `AttachmentReconciliationJob` sweeper (abandoned uploads, delete retries, opt-in storage orphan scan with dry run, purge).
  - Streamed downloads, SQL paging, SHA-256 content hash. See `wiki/Attachment/Reliability.md`.
- **New E2E** `AttachmentFlowTests`: upload → metadata → download → delete, plus cross-user upload (403) and HTML upload (400).
- **Tests:** 1,367 green across 12 projects.

## Wave 2 — Money correctness (2026-11-02)

Rules are documented in `wiki/Subscriptions/Billing.md`.
- **Tax:** one `TaxCalculator`, configurable `Subscriptions:Tax:PricesIncludeTax` + `Rate`; the tax breakdown is snapshotted on each invoice.
- **C11 refunds:** `RefundPolicy`, capped at money received, no double refund.
- **C12:** a unique index allows only one open subscription per subscriber (409 on a duplicate).
- **Invoices:**
  - The invoice is inserted in the same transaction as the approval.
  - **Gapless per-year numbering** (`invoice_number_counters` + `FOR UPDATE`).
  - Immutable issued invoices, with **credit notes** instead of a void flag.
  - PDF and email are produced by an idempotent Hangfire job from the snapshot.
- **Upgrades:** proration fixed (proof upload + admin approve/reject per payment; the SLA job reverts only unpaid upgrades through a credit note). Deferred proration is billed at renewal.
- **Renewals:** `RenewalOrchestrator` runs cancel → downgrade → past-due in order per subscription. The billing-date anchor is fixed with `CurrentPeriodStart` + `BillingPeriod`.
- **Smaller fixes:**
  - One `SubscriptionStatus` enum, stored as a string, with a state-machine table.
  - Approvals are bound to a specific `PaymentId`.
  - `ExpectVersion` on every admin money action.
  - Audit gains amount, currency and before/after JSON for every admin financial action.
- **Tests:** 175 unit + 18 PostgreSQL integration (concurrent create, double approval, 20 parallel approvals with no invoice gaps, refund cap, SLA revert, orchestrator ordering, snapshot PDF).

### Follow-ups created by Wave 2
- **Decided (owner):** `Plan.DiscountPercent` IS applied to charges. `Plan.Price` is the list price. Frontends must show the
  list price struck through plus the discounted price, never subtract the discount themselves.
- `SubscriptionStatus` numbers changed (Cancelled 3→4, Expired 4→3). Update frontend enums. Existing data needs a backfill if migrated from BateCom.
- The SLA job skips unpaid proration on an already PastDue subscription and leaves it for an admin (logged).

## Wave 1 — Security baseline (2026-11-02)

All critical items C1–C10 and C13 are fixed in StarterKit, each with tests. **BateCom still has every one of them.**
- **Identity (C1–C3):**
  - Hashed and attempt-capped OTP (`IOtpCodeService`).
  - `IRoleHierarchyGuard` on all 12 user/admin mutations; CreateAdmin is SuperAdmin-only.
  - `ISessionRevoker` plus refresh-token families with reuse detection and atomic rotation.
  - `IAuthSessionService` is the single token issuer.
  - JWT hardening (HS256 only, key length checked at startup).
  - Seed password comes from config, and must be changed on first login.
  - Self-service change-password.
  - Unique E.164 phone index.
- **Attachment (C4, C5, C13):**
  - `IAttachmentEntityAccessPolicy`, deny by default (Catalog registers Product).
  - Server-issued pending sessions.
  - Magic-byte, allowlist and megapixel validation; `IMalwareScanner`.
  - Signed read URLs + `/attachments/{id}/download`; no static `/uploads`.
  - Linux-safe Local storage with a traversal guard.
  - DB/blob consistency.
- **Email / Notification (C6, C7):**
  - HTML-encoded Liquid output, a `safe_url` filter, subject sanitizing.
  - `VerificationService`: HMAC, atomic Redis Lua counters, cooldown and daily cap, single use.
  - Idempotent dispatch with a single retry layer and permanent/transient/ambiguous error classes.
  - PII masking and log retention.
- **Shared (C8–C10):**
  - Per-client partitioned rate limiting, including new `auth`/`otp` policies.
  - Trusted forwarded headers only; `ASPNETCORE_FORWARDEDHEADERS_ENABLED` removed from compose.
  - Paging clamp, no `-1`, deterministic sort.
  - A single `GlobalExceptionHandler` with no DB leakage in 5xx.
  - Correlation id, response-wrapper fixes, CORS guard.
- **Integration fixes:**
  - Carter no longer scans validators (`WithEmptyValidators`): it registered open generic `BaseQueryValidator<>` and broke the container.
  - Per-module migrations history table (`MigrationsHistory`), the same at runtime and design time.
  - Generic Docker secrets provider.
  - New secrets `identity_otp_hmac_key` and `notification_verification_hmac_key`.
- **Catalog rebuilt** as the full reference module: 22 feature types (see `wiki/Modules/Catalog.md`).
- **Tests:** 1,101 green across 12 projects (unit, Testcontainers PostgreSQL/Redis integration, E2E).

### Follow-ups created by Wave 1
- **Email confirmation flow is missing.** Forgot-password now sends only to a *confirmed* email or *verified* phone, so
  email-registered users can't reset by email until confirm-email exists. **Do this early in Wave 5.**
- A manual re-enqueue of a notification sends the email **without attachments** (bytes are no longer stored in the log).
- Deploy notes: all users are signed out once (security-stamp hash). Phones must be unique and E.164 before the index
  migration. Clients must use server-issued attachment session ids and `X-Correlation-Id`. `pageSize=-1` is gone.

## Done (2026-11-01)

- **Optimistic concurrency fixed.** `AuditableEntityInterceptor` no longer overwrites `Version.OriginalValue`. A new
  helper `dbContext.ExpectVersion(entity, command.Version)` (`Shared.Core/Data/DbContextVersioningExtensions.cs`)
  replaces the hand-written `Entry(..).Property(..).OriginalValue = ...` boilerplate. Changes to an owned value
  object alone now bump the aggregate version and are concurrency-checked.
  - Guarded by `src/Shared/Shared.Core.Tests/Concurrency/*`: 15 tests on real PostgreSQL (Testcontainers).
    A mutation check confirmed that reintroducing the old bug fails 2 of them.
  - **BateCom still has the bug.** Port `AuditableEntityInterceptor.BumpVersion` + `ExpectVersion` there.
- **.NET 10 LTS + latest stable packages.** See the version policy comment at the top of `Directory.Packages.props`.
  `global.json` pins SDK 10.0.x. Dockerfile and CI moved to 10.0.
  - Breaking changes handled: Microsoft.OpenApi v2 (`SecuritySchemeDocumentTransformer`), deprecated
    `.WithOpenApi()` removed from 110 endpoints, OTel 1.20 EF instrumentation, the Figgle.Fonts split, QuestPDF 2026.9 font API.
  - Central transitive pinning is on (`CentralPackageTransitivePinningEnabled`). Roslyn is pinned to 5.0.0 for EF Design 10.
  - KubernetesClient is pinned to fix GHSA-w7r3-mgwf-4mqq (pulled in by Aspire).
- **Production placeholder guard.** `src/Api/Infrastructure/ProductionConfigurationGuard.cs` makes the app refuse to boot
  outside Development/Testing while any config value still contains `CHANGE_ME`. The error lists keys, never values.
- **Repo `nuget.config`** (nuget.org only). The template no longer depends on user-level private feeds.
- **Architecture tests** in `src/Api.Tests/Architecture/*`:
  - Modules reference each other only via `.Contracts`, and the shared kernel never references modules.
  - The CLAUDE.md naming conventions (Handler/Command/Query/Validator/Event/EndPoint) are enforced.

## P0 — Security

1. **Rotate the BateCom secrets.** BateCom tracks `.env`, `.env.dev`, `.env.prod` and `src/Api/appsettings*.json`
   in git, with real PostgreSQL, Redis, AWS, JWT, SMTP, WhatsApp and Hangfire credentials, plus a wiki page containing
   an AWS key. Rotate all of them, untrack the files, and consider purging history with `git filter-repo`.
   StarterKit contains placeholders only.

## P1 — Licensing decisions (deliberately pinned to the last free major)

2. ✅ **Decided 2026-11-04** ([Third-Party-Licenses](Third-Party-Licenses.md)):
   - **MediatR: removed.** It is replaced by the owned port `Shared.Contracts.Mediation` and its adapter
     `Shared.Core.Mediation.Mediator` ([ADR-0007](../Decisions/0007-owned-mediator-port.md)).
   - **MassTransit 8.5: kept** (Apache-2.0). Publishing is already behind the outbox, and only 6 consumers touch it.
     Re-decide (buy v9, or move to Wolverine/Rebus) when 8.x stops getting security fixes.
   - **FluentAssertions 7.2: kept** (Apache-2.0, test-only). The exit is `AwesomeAssertions`.
   - **QuestPDF:** the license is now configuration (`Subscriptions:Pdf:License`), no longer hard-coded `Community`.
2b. ✅ **Resolved 2026-11-04: replaced by SkiaSharp (MIT)**, no key and no secret ([Image processing](../Attachment/Image-Processing.md)). History: **SixLabors.ImageSharp 3.2** (Six Labors Split License) validates a license key in **Release** builds (Debug only
   warns), so `docker build` and the CI Release build fail without one.
   - 3.1.12, the last version that doesn't check a key, has several high-severity advisories. It's not an option for a module that parses user uploads.
   - The Dockerfile reads the key from the BuildKit secret `sixlabors_license`, and CI from the repo secret `SIXLABORS_LICENSE_KEY`.
   - **Decided (owner, 2026-11-03): use a license key.** Free for qualifying open-source and small companies, paid otherwise.
   - **Per project:** set the repo secret `SIXLABORS_LICENSE_KEY` and pass `--secret id=sixlabors_license,env=SIXLABORS_LICENSE_KEY` to local `docker build`. Never commit a `sixlabors.lic` file.

## P1 — Deferred major upgrades (breaking API, do one at a time with tests)

3. ~~**Mapster 7.4 → 10.x**~~ **Done** (2026-11-08, 10.0.14, no code changes needed). ImageSharp is gone (2b).
4. ~~**xUnit v2 → xUnit v3**~~ **Done** (2026-11-08, `xunit.v3` 4.x; test projects are executables, `IAsyncLifetime`
   returns `ValueTask`).
5. **AspNetCore.HealthChecks.* 9.0.0** is still the latest on NuGet (re-checked 2026-11-08, wave 14); blocked upstream. Dependabot
   proposes 10.x when it is released. Transitive pinning currently forces its EF dependency to 10.x.

## P1 — Platform / correctness

6. ~~**`PendingModelChangesWarning` is suppressed**~~ **Done** - the suppression is gone (a forgotten migration makes
   `Migrate()` throw) and `Api.E2ETests/Infrastructure/PendingModelChangesTests` calls `HasPendingModelChanges()` for
   every module DbContext of the booted Api host (own CI step "Check EF migrations are up to date" in `hybrid-ci-cd.yml`).
7. ~~**`Npgsql.EnableLegacyTimestampBehavior` is enabled globally.**~~ **Done** - the switch is removed everywhere
   (runtime + test fixtures); `DateTime` maps to `timestamptz` and must be `Kind=Utc`, `DateTimeOffset` must have
   offset 0 (normalize client input with `ToUniversalTime()`). Identity got migration `UseTimestampTz`.
8. ~~**Enable analyzers.**~~ **Done** - `Directory.Build.props` enables `latest-recommended` analyzers and
   `TreatWarningsAsErrors`; rule relaxations live in `.editorconfig` with a reason each (see
   [CodingStandards](../Architecture/CodingStandards.md#analyzers)). The remaining nullable warnings were fixed
   (`IQuery<T>` no longer requires `notnull`, so lookups can return `T?`), and the vulnerable transitive
   `System.Security.Cryptography.Xml/Pkcs` 8.0.x are pinned to 10.0.14.
9. ~~**Aggregate-level concurrency for child collections.**~~ **Done** (2026-11-08,
   [ADR 0021](../Decisions/0021-aggregate-version-covers-child-collections.md)): decided per aggregate;
   `BumpVersionOnSave()` for those whose child changes left the root untouched (SSO connection, tenant, feature flag).

## P1 — Tests

10. ~~**Identity and Attachment have no module tests.**~~ Done with G4 (`Identity.Tests`, `Identity.IntegrationTests`,
    `Attachment.Tests` including `Integration/`).
11. ~~CI should run the Testcontainers suites~~ Already covered: `hybrid-ci-cd.yml` runs `dotnet test StarterKit.slnx` on
    `ubuntu-latest` (Docker available), and `setup-dotnet` now reads `global.json`. Self-hosted jobs: the deploy job and
    `test-runner.yml` now run `deployment/scripts/verify-runner.sh` and fail with what is missing. Per server: install
    Docker on the runner ([guide](../deployment/Self-Hosted-Runner.md)) and run the workflow once.

## P2 — Template ergonomics

12. ~~**`dotnet new` template packaging.**~~ **Done** (G23): `dotnet new starterkit -n MyApp` with switches.
13. ~~**Module scaffolding.**~~ **Done** (G23): `dotnet new starterkit-module -n Orders`, no host wiring (ADR-0019).
14. ~~**Multi-tenancy decision.**~~ **Done** - optional multi-tenancy (Tenancy module: tenants, branches, members,
    invitations, tenant roles, audit; shared or dedicated databases; subscriber = tenant). See
    `wiki/MultiTenancy/Overview.md`. Follow-ups done: automated shared ⇄ dedicated move, data export, support
    impersonation, tenant-aware CORS, `tenant_id` on e-mail/notification logs (+ purge), tenant subscriber
    notifications → owner, invitation e-mails. Remaining:
    - Zero-downtime move (logical replication / CDC) - today the tenant is read-only for the copy.
    - Blobs in the data export (today only their metadata rows). Per-tenant storage containers: decided against by
      default in wave 14 ([Storage](../Attachment/Storage-Providers.md#tenant-isolation-one-container-a-prefix-per-tenant)).
    - ~~Aspire AppHost sample with a second database for a dedicated tenant~~ Done in wave 14
      (`MultiTenancy:DedicatedSample=true`).
15. **Per product, now in the [New-Project checklist](../Getting-Started/New-Project.md#product-decisions-before-the-first-production-migration)** (#15-17, #20).
    **Attachment enums.** `EntityType` / `AttachmentCategory` were reset (User/Product/Company/Other). Each project
    should edit them for its domain *before* its first production migration.
16. **Subscriber endpoints** use `.RequireAuthorization()` (any signed-in user) where BateCom used `OwnerOnly`.
    Revisit per project.
17. **WhatsApp templates** in `appsettings.json` are Arabic Meta template names. Make them per-project config.

## P1 — Follow-ups of the Notification / UserDirectory refactor

22. ~~**One OTP system.**~~ **Done:** Identity `IOtpCodeService` (PostgreSQL `identity.otp_codes`, HMAC with
    `identity_otp_hmac_key`, atomic issue gate + attempt counting) serves forgot-password, phone verification,
    onboarding and `/notifications/verification/*`; Notification only delivers (`IOtpDeliveryService`). The Redis
    `IVerificationService`, `Notification:Verification` and the `notification_verification_hmac_key` secret are gone.
    See `wiki/Identity/README.md#one-time-codes-otp`.
23. ~~**Role grants and live tokens.**~~ **Done:** every Identity role change (assign, revoke, promote, demote) now
    ends all sessions of the target (rotates the security stamp), so it applies immediately. See
    `wiki/Identity/Permissions-2FA-and-Admin-Audit.md`.
24. ~~**Runtime vs migration timestamp mapping.**~~ **Done** with #6 / #7.
25. ~~**Stale `src/Api/Dockerfile`.**~~ **Done** - the restore stage copies `global.json`, `nuget.config`, both
    `Directory.*.props` and the csproj of every project in the `Api.csproj` reference closure.

## P1 — Messaging follow-ups (integration events + outbox, 2026-11-03)

26. ✅ **Cross-instance L1 cache invalidation** (2026-11-04: Redis broadcast). `TenantStore` / `TenantMembershipResolver` keep an in-process L1 cache;
    invalidation reaches only the instance that made the change, the others wait `MultiTenancy:Cache:LocalSeconds`
    (e.g. a suspended tenant keeps working there until then). Broadcast invalidations (Redis pub/sub or a per-instance
    bus endpoint) once the app runs multiple replicas.
27. ~~**Durable saga repository.**~~ **Done** (2026-11-08, G21,
    [ADR 0023](../Decisions/0023-durable-sagas-on-module-dbcontexts.md)): no saga scanning, no in-memory provider;
    `AddDurableSagaStateMachine` in the module's `ConfigureMessaging` (EF Core repository on the module DbContext).
28. ✅ **Outbox admin tooling** (2026-11-04: endpoint, metrics, alerts). Dead letters are inspected/requeued with SQL (`wiki/Architecture/Messaging.md`). Add a
    platform-admin endpoint + metric (pending / dead-lettered per context) and alert on dead letters.
29. ~~**Production transport.**~~ **Done** (2026-11-08, [ADR 0022](../Decisions/0022-production-message-transport.md)):
    PostgreSql by default, RabbitMq by choice, `--migrate-only` owns the SQL-transport schema.

## P2 — Ops

18. ✅ **CI variables** (2026-11-04: fail-fast added). The workflows read `vars.APP_NAME`, `DEPLOY_DIR`, `PUBLIC_API_URL`, `POSTGRES_*` and `BACKUP_BUCKET`.
    They are documented in `wiki/deployment/CI-CD.md#github-setup` and the deploy job fails immediately when one is missing.
19. ✅ **Health / readiness** (2026-11-04). Split `/health/live` and `/health/ready` (DB, Redis, Seq) for compose and K8s, and wire them into the deploy health check.
20. **Per product (New-Project checklist).** **Nginx admin allow-list.** `deployment/nginx/conf.d/app-production.conf` allows only `127.0.0.1` for admin paths. Set it per deployment.
21. ~~**Observability stack** is heavy.~~ **Done** (2026-11-08, G24): `docker-compose.observability-lite.yml`
    (Prometheus scraping the API `/metrics` + Grafana, the same alerts; logs and traces stay in Seq).

## P2 — Found during the docs/tooling overhaul (2026-11-04)

22. ✅ **MediatR tracing** (2026-11-04): the buggy, unregistered `ObservabilityPipelineBehavior` + `CoreDiagnostics/*`
    (per-request assembly scans, swapped command/query, `Elapsed.Seconds`, disposing `Activity.Current`) were replaced by
    `RequestTelemetryBehavior` (one span + `mediatr.request.duration` histogram per request, outermost, host-only).
    The same `Activity.Current` disposal bug was fixed in `CustomeDiagnosticsProvider.ExecuteActivityAsync`.
23. ✅ **Deny-by-default authorization** (2026-11-04): `RequireAuthorizationByDefault()` on the Carter group (an endpoint
    without auth metadata requires an authenticated user) + `Every_endpoint_declares_authorization_explicitly`
    architecture test. Not a global `FallbackPolicy`, which would also block middleware paths (Hangfire dashboard Basic
    auth, Swagger UI) and framework endpoints (health, OpenAPI, Scalar).
24. ✅ **Ports** (2026-11-04): `.env.example` `API_PORT` 8888 → 5000 (8888 clashed with the OTel collector); Mailhog is
    8025/1025 everywhere. PostgreSQL/Redis/Seq keep deliberate dev offsets in `.env.example` (documented).
25. ✅ **Integration coverage** (2026-11-04): new `Tenancy.IntegrationTests` (real PostgreSQL + migrations + module);
    `TenantRowLevelSecurityCoverageTests` (every tenant table of every tenant-data module has forced RLS);
    `ClientGeneratedKeyTests`. They found and fixed: **adding a tenant custom domain always failed with 409** (owned
    `TenantDomain` key not `ValueGeneratedNever`), and the same latent trap on `PlanFeature`. Also fixed: the committed
    AppHost `Messaging:Transport=RabbitMq` broke two `AppHost.Tests` (default restored to `InMemory`).
26. ✅ **Notification + Attachment real-database coverage** (2026-11-04):
    - `Notification.Tests/Persistence/NotificationDispatchPostgresTests`: the dispatch job on real PostgreSQL. Covers no
      re-send after delivery, transient attempts counted across runs, fallback, admin re-enqueue, stored channel order.
    - `Attachment.Tests/Integration/AttachmentTenantIsolationTests`: two tenants, real migrations, a non-superuser
      connection so RLS is enforced. Covers no cross-tenant read, list or delete; tenant-rooted blobs.
    Both stay inside each module's existing `.Tests` project (that module's convention) rather than adding new projects.
27. ✅ **License** (2026-11-04): MIT (`LICENSE`).
