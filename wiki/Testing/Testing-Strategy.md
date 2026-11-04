# Testing strategy

**Well-tested code is non-negotiable.** Every feature is covered at more than one level, every failure mode has a
test, and the architecture tests keep the design honest. Copy-ready templates: `/starterkit:write-tests`
(`tools/claude/starterkit-plugin/skills/write-tests/`).

## The test pyramid in this repo

| Level | Project(s) | Speed / infra | What belongs here |
|---|---|---|---|
| **Unit: domain** | `src/Modules/<M>/<M>.Tests/Domain` | ms, none | Factories, invariants, every state-machine transition (Theory over status × action), events raised |
| **Unit: features** | `<M>.Tests/Features`, `Services` | ms, none | Validators (en + ar messages, resx key parity), handler branches with Moq (not found, forbidden, conflict), services, mappers |
| **Integration** | `<M>.IntegrationTests` (Catalog, Identity, Subscriptions, Tenancy); Attachment's in `Attachment.Tests/Integration` (incl. two-tenant RLS as a non-superuser) and Notification's in `Notification.Tests/Persistence` | s, Docker | Real `Add<M>Module` + Testcontainers `postgres:16-alpine` + real migrations: SQL filters, paging, unique indexes, optimistic concurrency, soft delete, tenant isolation, outbox rows, jobs, seeder idempotency |
| **Shared kernel** | `src/Shared/Shared.Core.Tests` | ms to s | Interceptors, versioning (`Concurrency/*` on Postgres), outbox, multi-tenancy, caching, paging, health checks |
| **Architecture** | `src/Api.Tests/Architecture` | s, none | Module boundaries, naming, messaging, tenancy, background-job dispatch, explicit auth on every endpoint, client-generated child keys (`ClientGeneratedKeyTests`) |
| **HTTP E2E** | `src/Api.E2ETests` | s, Docker | The real API over HTTP: routing, auth (401/403), envelopes, cross-module flows, `PendingModelChangesTests`, `TenantRowLevelSecurityCoverageTests` (forced RLS on every tenant table of the real migrations) |
| **Multi-tenant E2E** | `src/Api.E2ETests/MultiTenancy` | s, Docker | `MultiTenantE2EFixture`: tenant A can never read or modify tenant B |
| **Orchestration** | `src/Aspire/AppHost.Tests` | s to min, Docker | AppHost model and settings; live startup |
| **Browser smoke** | `tests/e2e` (Playwright, TS) | min, running stack | A handful of critical journeys, with Mailhog helpers for emails |

Stack: **xUnit 2, FluentAssertions 7, Moq 4, Testcontainers, Microsoft.AspNetCore.Mvc.Testing**. Don't introduce
other assertion or mocking libraries.

## What every feature needs

| Change | Minimum tests |
|---|---|
| New aggregate method or invariant | Domain unit tests: happy path, each guard, events raised |
| New command | Validator tests; handler tests for success + not found + forbidden + conflict; integration test if it touches SQL, uniqueness, concurrency or tenancy |
| New query | Integration test for filters, sorting, paging and tenant/branch/soft-delete filtering |
| New endpoint | E2E: 401 anonymous, 403 wrong role or permission, happy path |
| Critical update | Integration: stale `Version` throws `ConcurrencyConflictException` and changes nothing; current version bumps by one |
| Integration event | Aggregate raises it; outbox row written after commit; consumer is idempotent (duplicate = no-op) |
| Background job | Integration: only eligible rows processed; one failure doesn't stop the batch; events published |
| Tenant data | Multi-tenant E2E or integration: cross-tenant read and write impossible |
| New module, event or permission | Architecture test registries updated (`ModuleBoundaryTests.Modules`, event list, outbox list) |

## Conventions

- Name tests `Scenario_Expectation`, for example `Update_WithStaleVersion_IsRejected_AndNothingChanges`.
- Use Arrange / Act / Assert with one behaviour per test. Theories for matrices.
- Build data through **real** factories and commands. `TestSupport` builders walk real state machines; no
  reflection.
- Integration tests share one container per collection. Isolate by using fresh ids and unique values (SKUs,
  emails).
- Assert outcomes **and** side effects: DB state, events, outbox rows, version, "nothing changed" on failure.
- Fake time with `TimeProvider`; never `Thread.Sleep` or `Task.Delay` in tests.
- Tests may call `IgnoreQueryFilters()` to inspect raw data. Production code may not.

## Running

```bash
dotnet test StarterKit.slnx                                          # all (Docker running)
dotnet test src/Api.Tests                                            # guardrails, fast
dotnet test src/Modules/Catalog/Catalog.Tests --filter "FullyQualifiedName~ProductTests"
dotnet test src/Modules/Catalog/Catalog.IntegrationTests --filter "FullyQualifiedName~Update_WithStaleVersion"
dotnet test StarterKit.slnx --collect:"XPlat Code Coverage"         # coverlet
cd tests/e2e && npm ci && npx playwright test                       # against a running stack
```

CI (`hybrid-ci-cd.yml`) runs `PendingModelChangesTests` and then the whole solution on every push and PR. A red
architecture test is a design problem, not a flaky test. Fix the code, or change the rule deliberately with an ADR.
