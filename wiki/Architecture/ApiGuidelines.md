# API guidelines

These are the HTTP API conventions. Endpoints are Carter minimal APIs (`ICarterModule`); there are no controllers.
To add one, use the copy-ready templates in `/starterkit:add-feature`
(`tools/claude/starterkit-plugin/skills/add-feature/references/templates.md`).

## Routes and versioning

- Business endpoints: `EndpointConfig.BaseApiPath + "/<resources>"`, which resolves to `api/v{version}/<resources>`.
  For example, `GET api/v1/products`, `PUT api/v1/products/{id:guid}`.
- Use plural nouns and kebab-case segments. Actions that aren't CRUD are sub-resources with verbs:
  `POST api/v1/products/{id}/publish`, `POST .../submit-for-review`.
- Route constraints are always typed (`{id:guid}`). The route id is the source of truth; the body never carries the
  id.
- Each endpoint declares `.WithApiVersionSet(app.NewApiVersionSet("<Module>").Build())` and `.HasApiVersion(1.0)`.
  The version is read from the URL segment or the `api-version` header (`Shared.Core/Web/ApiVersioningExtensions.cs`).
  A breaking change ships as a new version, and the old one is kept until clients migrate.
- Some older Identity and admin routes (`api/auth/*`, `api/identity/*`, `/admin/...`) are unversioned. Keep new work
  on the versioned base path.

| Verb | Use | Success |
|---|---|---|
| GET | Read, list (paginated) | 200 |
| POST | Create (`Results.Created(location, ...)`) or action | 201 / 200 |
| PUT | Full update (with optional `version`) | 200 |
| PATCH | Narrow state change (`/status`) | 200 |
| DELETE | Delete (soft or hard) | 200 |

## Request and response shape

- **Requests:** a `<Feature>Request` record in the endpoint file, mapped explicitly to the command. Queries bind
  with `[AsParameters]` to a `BaseQuery`-derived record (`PageNumber` default 1, `PageSize` default 10, sort fields).
- **Responses:** always the `ApiResponse<T>` envelope via `ApiResponseExtensions.Success(data)`:

  ```json
  { "isSuccess": true, "statusCode": 200, "data": { ... }, "successMessage": null, "errors": [], "traceId": "..." }
  ```

- **Lists** return `PaginatedResult<T>` (`pageInfoResult { currentPage, pageSize, totalItems, ... }`, `result: []`).
  Unbounded lists are not allowed; small fixed lookups may return a plain list.
- **Errors** are produced only by `GlobalExceptionHandler` from typed exceptions, in the same envelope:

  ```json
  { "isSuccess": false, "statusCode": 409, "errors": ["A product with SKU 'X' already exists."], "traceId": "...", "code": null }
  ```

  | Exception | Status |
  |---|---|
  | `BadRequestException`, `DomainException`, validation failure | 400 (validation messages localized by `Accept-Language`) |
  | `UnauthorizedException` / no token | 401 |
  | `ForbiddenException` / failed policy | 403 |
  | `NotFoundException` | 404 |
  | `ConflictException`, `ConcurrencyConflictException` (stale `version`) | 409 |
  | `GoneException` | 410 |
  | rate limit | 429 |
  | anything else | 500 (generic message + `traceId`; details only in logs) |

- **Enums** are serialized as strings. Their localized labels come from the enum lookup endpoint.
- **Bilingual input and output** uses `LocalizedText` `{ "en": "...", "ar": "..." }`.
- **Optimistic concurrency:** read DTOs expose `version`; update requests may send it back. A stale one returns 409.

## Documentation metadata

Every endpoint declares `.WithName`, `.WithTags`, `.WithSummary`, `.WithDescription`, and one `.Produces<...>` per
status it can return. The API serves Swagger UI at `/swagger` and Scalar at `/scalar`; restrict both in production
through Nginx.

## Security

- **Auth is explicit.** Carter endpoints are deny-by-default (authenticated user) as a safety net, and
  `Every_endpoint_declares_authorization_explicitly` fails the build unless each endpoint states its intent. Pick from the table in
  `CLAUDE.md` → *Hard-won patterns* (`RequireAuthorization`, `"PageGate"`, `RequirePermission`,
  `RequireTenantPermission`, `HostOnly`, `DisallowImpersonation`, `AllowAnonymous`). See also
  `/starterkit:add-permission`.
- **Rate limiting:** `.RequireRateLimiting("create" | "search" | "upload" | "auth" | "otp")`. Policies are
  configured in `RateLimiting:Policies`, partitioned per user and otherwise per client IP. Login, OTP and upload
  endpoints must have one.
- **CORS** is configured per environment (`Cors` section, `UseConfigurableCors`).
- Never return internal ids of other tenants, stack traces, or whether an account exists (forgot-password
  always returns 200).

## Checklist for a new endpoint

- [ ] Versioned route, typed constraints, plural resource
- [ ] Request record mapped to a command or query; no domain types in the HTTP contract
- [ ] Validator with localized messages (en + ar)
- [ ] Explicit authorization and rate limiting
- [ ] Full `Produces` list, plus name, tags and summary
- [ ] `PaginatedResult<T>` for lists; `version` round-trip for critical updates
- [ ] Tests: validator, handler, and E2E for 401/403 plus the happy path (`/starterkit:write-tests`)

Related: [DesignPatterns.md](DesignPatterns.md) · [DatabaseConventions.md](DatabaseConventions.md) ·
[../Testing/Testing-Strategy.md](../Testing/Testing-Strategy.md)
