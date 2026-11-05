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
