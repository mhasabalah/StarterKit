# 0013. Run per-tenant SSO and SCIM inside Identity, without dynamic auth schemes

## Status

- **Status:** Accepted
- **Date:** 2026-11-05

## Context

B2B customers expect to sign in through their own IdP (OIDC or SAML 2.0), to provision and deprovision users from
their directory (SCIM 2.0), and to enforce SSO for their domain. Connections are created at runtime by tenant
admins, so their configuration is data, not startup configuration. The main risks are account takeover (a tenant
claiming someone else's email), open redirects / token leakage on the browser hand-off, SSRF through
admin-supplied IdP URLs, and replay.

## Decision

- SSO and SCIM live in the **Identity** module (`Domains/Sso`, `Domains/Scim`): token issuance and `UserManager`
  are Identity-internal. Memberships are changed only through Tenancy's new `ProvisionMemberCommand`.
- **No per-tenant ASP.NET authentication schemes.** The protocol flows run in handlers with per-connection
  configuration: OIDC on `Microsoft.IdentityModel.Protocols.OpenIdConnect` (code + PKCE + nonce, local id_token
  validation), SAML on **ITfoxtec.Identity.Saml2** (BSD-3-Clause; built for per-request configuration).
- Email-based linking and JIT only for **DNS-verified domains**, each verifiable by one connection platform-wide.
- Browser hand-off through a single-use 60-second **exchange code**; return URLs on an allow-list; server-side,
  single-use state.
- Outbound IdP calls use the shared SSRF guard (moved from Webhooks to `Shared.Core.Net`).
- SCIM authenticates with per-directory bearer tokens (hash at rest) through its own scheme/policy; deprovisioning
  suspends the membership and revokes sessions, never deletes.

## Consequences

- One code path for all tenants; adding a connection needs no restart.
- Identity now depends on `Tenancy.Contracts` for provisioning (allowed: Contracts + `ISender`).
- The plan feature `sso` and the tenant permission `tenant.sso.manage` gate administration.
- Tests: SAML responses signed by a test IdP; an in-process OIDC provider in E2E; Okta/Entra-shaped SCIM traffic.

## Alternatives considered

- **Dynamic `AddOpenIdConnect`/`AddSaml2` schemes per tenant** (`IOptionsMonitorCache` tricks): fragile, cookie-
  based, hard to test, scheme explosion with many tenants.
- **Sustainsys.Saml2** (MIT): excellent as an ASP.NET handler, less convenient for per-request configuration
  without schemes.
- **External broker** (Keycloak, Auth0, WorkOS): less code but a paid or operated dependency and a second user
  store; still possible behind `UserDirectory.Contracts` ([ADR 0006](0006-identity-behind-userdirectory-port.md)).

## Related

[Enterprise SSO](../Identity/Enterprise-SSO.md), [SCIM](../Identity/SCIM.md).
