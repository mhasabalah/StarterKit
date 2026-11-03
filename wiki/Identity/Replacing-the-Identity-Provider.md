# Replacing the Identity Provider (e.g. Keycloak)

The Identity module is ONE implementation of two provider-neutral seams. Everything else in the system depends on the
seams, never on Identity, so a replacement (Keycloak, Entra ID, Auth0, ...) is an adapter module plus host wiring.

## The seams

1. **`UserDirectory.Contracts`** (`src/Modules/Identity/UserDirectory.Contracts`) - the only Identity surface other
   modules reference (enforced by `ModuleBoundaryTests.Only_Identity_references_Identity_Contracts`):

   | Contract | Used by |
   |---|---|
   | `GetUserSummariesQuery` -> `UserSummaryDto` (batch, max 500) | Tenancy (members, invitations, export) |
   | `GetUserProfileQuery` -> `UserProfileDto?` (null when unknown) | Subscriptions / Tenancy subscriber info |
   | `GetUserContactInfoQuery` -> `UserContactInfoDto` | Notification (recipient email/phone), Tenancy |
   | `GetUsersInRoleQuery` -> active users of a platform role | Subscriptions admin billing emails |
   | `GetUserStatsQuery` | Api dashboard |
   | `ImpersonationStartedEvent` (publish) | Tenancy audit trail |

   `DisplayName` rule: "First Last", else the email, else the id - never empty.

2. **Role claims in the access token.** Platform permissions are NOT in tokens: `PermissionClaimsTransformation`
   (`Shared.Core/Authorization`) expands the principal's role claims through `RolePermissionMap` on every request,
   for every authentication scheme. `.RequirePermission(...)`, `IHttpContextService.HasClaim(Permissions.ClaimType, ...)`
   and the single-tenant fallback of tenant permissions all read those derived claims. Tenant permissions come from
   Tenancy memberships (database), not from the token.

## What an adapter must provide

| Concern | Identity module today | Keycloak adapter |
|---|---|---|
| Token validation | HS256 JwtBearer (`IdentityModule`) | JwtBearer with `Authority` = realm, `RoleClaimType` = the claim your realm-role mapper emits (e.g. `roles`) |
| Roles | `ClaimTypes.Role` per role | realm roles named exactly like `RoleConstants` (`User`, `Admin`, `SuperAdmin`, `Finance`, `Support`) |
| Permissions | derived by `AddRolePermissionClaims()` | call `services.AddRolePermissionClaims()` - nothing else |
| 2FA rule for privileged roles | `amr` contains `mfa` (`AuthenticationMethods.IsMultiFactor`) | map Keycloak's `amr`/`acr` (step-up / LoA) to `amr=mfa` |
| User id | `sub` / `NameIdentifier` = user Guid | `sub` (Keycloak ids are UUIDs) - keep ids stable, other modules store them |
| `UserDirectory.Contracts` handlers | EF queries on `identity.users` | a local `users` projection synced from Keycloak admin events (or on first login); never call Keycloak per request |
| Active tenant | `tid` + `tms` (membership stamp) claims, re-issued by `SwitchTenant` | resolve the tenant per request (header/subdomain, `MultiTenancy:Resolution`) or a token-exchange step that adds `tid` |
| Session revocation | security stamp (`sstamp`) checked against Redis | Keycloak session revocation + short access-token lifetime |
| Impersonation | custom token with `act` | Keycloak token exchange; publish `ImpersonationStartedEvent`; keep `act` |
| Onboarding / OTP / password flows | Identity endpoints | Keycloak login flows; drop the Identity endpoints |

`Identity.Contracts` (login, register, admin workflows) is private to Identity and the host and is replaced as a whole.

## Checklist

1. New module `src/Modules/<Provider>Identity` implementing every `UserDirectory.Contracts` query handler.
2. Host: replace `AddIdentityModule` / `UseIdentityModule`; keep `AddIdentityAuthorizationPolicies` (or move the
   policy registration with it) and `AddRolePermissionClaims()`.
3. Add the module to `ModuleBoundaryTests.Modules`; run `dotnet test` - the architecture tests prove no module
   depends on the old Identity.
4. E2E: `TestAuthHandler` only emits role claims, so the suite is provider-independent.
