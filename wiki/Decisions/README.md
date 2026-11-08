# Architecture Decision Records

Short records of the significant decisions behind StarterKit. Add one whenever you change a rule in `CLAUDE.md`, swap
a core library, or change an architecture test. Copy [0000-template.md](0000-template.md), take the next number, and
link it here.

| # | Decision | Status |
|---|---|---|
| [0001](0001-modular-monolith-schema-per-module.md) | Modular monolith, one schema and DbContext per module | Accepted |
| [0002](0002-carter-minimal-apis.md) | Carter minimal APIs instead of controllers | Accepted |
| [0003](0003-exceptions-not-result-type.md) | Typed exceptions mapped centrally, no Result type | Accepted |
| [0004](0004-outbox-and-messaging-transports.md) | Transactional outbox, pluggable MassTransit transport | Accepted |
| [0005](0005-tenant-isolation-model.md) | Tenant isolation: named filters + RLS, shared or dedicated DBs | Accepted |
| [0006](0006-identity-behind-userdirectory-port.md) | Users only through the UserDirectory port | Accepted |
| [0007](0007-owned-mediator-port.md) | Own the mediator port; do not depend on MediatR | Accepted |
| [0012](0012-realtime-inbox-and-push.md) | In-app inbox, SignalR hub and FCM push as additive notification channels | Accepted |
| [0013](0013-enterprise-sso-and-scim.md) | Per-tenant SSO (OIDC/SAML) and SCIM inside Identity, without dynamic auth schemes | Accepted |
| [0014](0014-passkeys-on-built-in-identity.md) | Passkeys on .NET 10 Identity's built-in WebAuthn, server-side ceremony state | Accepted |
| [0015](0015-malware-scanning-clamd-fail-closed.md) | Upload malware scanning with ClamAV over an own TCP client, failing closed | Accepted |
| [0017](0017-money-precision-and-per-currency-rounding.md) | Money columns are numeric(19,4); rounding and scale are per ISO 4217 currency in the domain | Accepted |
| [0018](0018-off-session-dunning-retries-on-saved-cards.md) | Idempotent off-session card retries before the dunning reminder; sandbox/live gateway mode | Accepted |
| [0016](0016-bulk-data-transfer-on-hangfire.md) | Bulk import/export as leased, checkpointed Hangfire jobs on their own queue (same image, role switch) | Accepted |
<!--#if (templateSource)-->
| [0020](0020-dotnet-new-templates.md) | The repository is the product template; modules come from a generated, CI-verified module template | Accepted |
<!--#endif-->
| [0019](0019-module-manifests-and-discovery.md) | Modules plug in through a manifest the host discovers from the build graph (no Program.cs edits) | Accepted |
| [0021](0021-aggregate-version-covers-child-collections.md) | The aggregate version covers its child collections, decided per aggregate | Accepted |
| [0022](0022-production-message-transport.md) | PostgreSQL is the default production transport; the one-shot migrator owns its schema | Accepted |
