# 0006. Other modules see users only through the UserDirectory port

## Status

- **Status:** Accepted
- **Date:** 2026-11-03

## Context

Many products eventually replace the built-in ASP.NET Identity with an external IdP (Keycloak, Entra ID, Auth0).
If modules depend on Identity's commands and models, that swap touches everything.

## Decision

`UserDirectory.Contracts` is a provider-neutral port: `GetUserSummariesQuery`, `GetUserProfileQuery`,
`GetUserContactInfoQuery`, `GetUsersInRoleQuery`, `GetUserStatsQuery` and user events. Identity implements it.
`Identity.Contracts` (auth and admin workflows) is private to Identity and the host.

## Consequences

- Replacing the identity provider means writing a new adapter for one port. See
  [Replacing-the-Identity-Provider](../Identity/Replacing-the-Identity-Provider.md).
- Modules can't reach identity-specific features directly; add them to the port deliberately.
- **Enforced by:** `ModuleBoundaryTests` (only Identity and the host may reference `Identity.Contracts`; the port
  depends only on the shared kernel).

## Alternatives considered

- **Modules referencing `Identity.Contracts`:** simpler today, but expensive to swap later.

## Related

[Replacing the Identity Provider](../Identity/Replacing-the-Identity-Provider.md).
