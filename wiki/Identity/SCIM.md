# SCIM 2.0 provisioning

The customer's directory (Entra ID, Okta, Google Workspace, JumpCloud, ...) creates, updates and deactivates users
and groups automatically (RFC 7643 / 7644). Backlog item G15 (with [Enterprise SSO](Enterprise-SSO.md)).
Decision record: [ADR 0013](../Decisions/0013-enterprise-sso-and-scim.md).

## Setup

1. Verify the company's email domain on an [SSO connection](Enterprise-SSO.md) (SCIM only provisions verified domains).
2. `POST /api/v1/tenant/scim/directories {name}` → **base URL** (`https://api.example.com/scim/v2`) and **token**
   (`scim_<prefix>_<secret>`, shown once; only a SHA-256 hash is stored).
3. Paste both into the IdP's provisioning app (Entra: *Provisioning → Tenant URL + Secret token*; Okta: *SCIM 2.0
   Bearer token*). Optionally map pushed groups to tenant roles.

Admin routes (`/api/v1/tenant/scim/directories`, same gates as SSO: `tenant.sso.manage` + plan feature `sso`):
list, create, `POST {id}/rotate-token` (old token dies immediately), `PUT {id}/enabled`, `DELETE {id}`,
`GET {id}/groups`, `PUT {id}/groups/{groupId}/role`. At most 5 directories per tenant.

## Protocol surface (`/scim/v2`)

| Resource | Operations |
|---|---|
| `ServiceProviderConfig`, `ResourceTypes`, `Schemas` | GET (advertise exactly what is implemented: PATCH, filter, ETag; no bulk, sort or password change) |
| `Users` | GET (filter, `startIndex`/`count` ≤ 200), GET by id, POST, PUT, PATCH, DELETE |
| `Groups` | GET (filter, `excludedAttributes=members`), GET by id, POST, PUT, PATCH, DELETE |

Requires `Identity:Sso:Enabled` and the tenant plan feature `sso` (SCIM 403 otherwise).

Authentication: `Authorization: Bearer scim_…` through its **own** scheme and policy (`ScimAuthentication`): a SCIM
token authenticates nothing else, and a JWT or API key never reaches SCIM. Every request runs in the directory's
tenant scope. Responses are `application/scim+json`; errors use SCIM's error schema (`status`, `scimType`, `detail`),
never the platform envelope.

Filters: `attr eq "value"` joined by `and`, on `userName`, `externalId`, `id`, `displayName`, `emails.value` and
`emails[type eq "work"].value`. Anything else is 400 `invalidFilter` (never silently "match all").

## Semantics

| Situation | Behaviour |
|---|---|
| POST user | `userName` and `externalId` unique per directory (409 `uniqueness`, also unique indexes for concurrent retries). Email must be on a verified domain of the tenant (400 `invalidValue`). Existing account with that email → linked; else created (email confirmed, no password). Membership created via Tenancy `ProvisionMemberCommand` (seat limit `MaxUsers` applies). |
| `active: false` (PATCH/PUT) and DELETE | Membership **suspended** in this tenant and **every session revoked** immediately. Nothing is deleted (GDPR erasure is the explicit account-deletion flow). DELETE removes the SCIM resource (GET → 404). |
| PATCH | Op names any case; path-less `replace` with an object (Okta); `"active": "False"` strings and `name.givenName` paths (Entra). Enterprise paths (`urn:ietf:params:scim:schemas:extension:enterprise:2.0:User:department`, `...:manager`, `manager.value`, or a path-less object keyed by the extension URN). A path that is not an attribute of the User or enterprise schema is a 400 `invalidPath` (RFC 7644 section 3.5.2), never silently ignored. Attributes of RFC 7643 that this server does not store (phone numbers, addresses, `title`, ...) are known, so they are accepted and ignored: Entra and Okta send them whenever an admin maps them. |
| Enterprise extension | `employeeNumber`, `costCenter`, `organization`, `division`, `department`, `manager` are stored per directory user and returned under the extension URN (which is then listed in `schemas`). `manager.value` is the SCIM id of another user of the SAME directory; an unknown manager is dropped (IdPs often send it before the manager exists). POST/PUT read the extension from the body (PUT replaces it when present and leaves it alone when absent); an empty value or `remove` clears an attribute. Discovery lists it in `Schemas` and `ResourceTypes.schemaExtensions`. |
| Groups | Members must be users of the same directory. `add`/`remove` are incremental, including `members[value eq "<id>"]`. |
| Roles | Unmapped directory: roles are managed in the app and never touched. Once any group is mapped, the directory **owns** roles: a user's roles = mapped roles of their groups (re-applied on every membership or mapping change). A mapping must name a role of the tenant (validated through Tenancy, 400 `tenant_role_not_found`). |
| Owner | A directory never suspends the tenant owner (break-glass). |
| Isolation | A directory sees only its own resources (another directory's ids answer 404). |
| Concurrency | Users and groups carry a revision (concurrency token). Parallel PATCHes from the IdP are retried on fresh data (3 attempts), so no add/remove is lost. `meta.version` is a weak ETag; `If-Match` is not evaluated (advertised `etag.supported=false`). |
| Sessions | Deactivation revokes **all** sessions of the account (security first), not only those of this tenant. |

## Code map

- Model: [`ScimModels.cs`](../../src/Modules/Identity/Identity/Domains/Scim/Models/ScimModels.cs) (`ScimDirectory`, `ScimUser`, `ScimGroup`; tables `identity.scim_*`).
- Semantics: [`ScimProvisioningService`](../../src/Modules/Identity/Identity/Services/Scim/ScimProvisioningService.cs); protocol helpers and filter parser: [`ScimSupport.cs`](../../src/Modules/Identity/Identity/Services/Scim/ScimSupport.cs).
- Endpoints: `Domains/Scim/Features/ScimProtocol/ScimModule.cs`, `Domains/Scim/Features/ScimDirectories/ScimDirectoriesModule.cs`.
- Admin commands use `IScimDirectoryRepository`; the directory list counts users/groups in the same SQL query.
- Tests: [`Api.E2ETests/Identity/ScimE2ETests.cs`](../../src/Api.E2ETests/Identity/ScimE2ETests.cs) (Okta- and Entra-shaped requests, role mapping, 401/403, disabled directories) and `Tenancy.IntegrationTests/ProvisionMemberTests.cs` (idempotency, seat limit, owner rule).

## Known limits

No `/Bulk`, no sorting, no `/Me`, no custom schema extensions beyond the enterprise one. Email changes from the
directory are not applied to existing accounts (the account email is a credential; change it in the app).
