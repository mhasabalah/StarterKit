---
name: write-tests
description: Write tests for StarterKit code at the right level - domain/handler/validator unit tests (xUnit + FluentAssertions + Moq), module integration tests on real Postgres (Testcontainers + real Add<Module>Module), HTTP end-to-end tests (Api.E2ETests, multi-tenant fixture), architecture tests and Playwright smoke tests; plus how to run a single test. Use for "add tests", "test this handler", "integration test", "e2e test", "increase coverage", "why is this test failing".
---

# Write tests

**Well-tested code is non-negotiable here.** Every feature gets tests at more than one level, and every
failure mode has a test: not found, forbidden, conflict, validation, stale version, cross-tenant.

## Pick the level

| Level | Project | Use for | Infra |
|---|---|---|---|
| Domain unit | `src/Modules/<M>/<M>.Tests/Domain/` | Factories, invariants, state machines, events raised | none |
| Handler/validator unit | `<M>.Tests/Features/` | Branching in handlers (Moq repos/services), validator rules and localized messages | none |
| Module integration | `src/Modules/<M>/<M>.IntegrationTests/` | Real SQL: queries, filters, unique indexes, concurrency (409), outbox rows, tenant isolation, jobs | Docker (Testcontainers `postgres:16-alpine`) |
| HTTP E2E | `src/Api.E2ETests/<M>/<Flow>Tests.cs` | Full API: routing, auth (401/403), versioning, envelopes, cross-module flows | Docker (`E2ETestFixture` boots the API, which auto-migrates) |
| Multi-tenant E2E | `src/Api.E2ETests/MultiTenancy/` | Tenant A cannot see or modify tenant B data | Docker (`MultiTenantE2EFixture`) |
| Architecture | `src/Api.Tests/Architecture/` | Rules for every module; update registries when adding modules or events | none |
| Shared kernel | `src/Shared/Shared.Core.Tests/` | Interceptors, concurrency, outbox internals | Docker for `Concurrency/*` |
| Browser smoke | `tests/e2e/` (Playwright, TypeScript) | A few critical journeys against a running stack | Running API + Mailhog |

## Conventions

- **Stack:** xUnit 2, FluentAssertions 7, Moq 4. Bogus is available but unused. Don't add NSubstitute or Shouldly.
- **Names:** `Scenario_Expectation` or `Method_Condition_Result`, for example
  `Update_WithStaleVersion_IsRejected_AndNothingChanges` or `DuplicateSku_Throws409_AndDoesNotAdd`.
- **Builders:** put them in `TestSupport.cs` (`New<X>()`, `<X>In(status)` walking the real state machine,
  `Localizer()` over the real resx, `DisconnectedDbContext()` for `ExpectVersion`, `Http(userId, roles)`).
  No reflection hacks.
- **Integration tests:** share one container per collection (`[Collection(<M>PostgresCollection.Name)]`). Each test
  uses fresh ids and unique values, so the tests stay independent. Build data through real commands (`Scenario`),
  not raw inserts.
- **Assertions:** assert outcomes *and* side effects: DB state, events raised, outbox rows, version bump, and that
  nothing changed on failure.
- **Tenancy in unit tests:** use `TenantContext.SingleTenant` or `TenantScope.Begin(...)`.
- **Time:** inject a fake `TimeProvider` (`Microsoft.Extensions.Time.Testing.FakeTimeProvider` if referenced, or a
  small stub). Never sleep.
- Tests may use `IgnoreQueryFilters()` to inspect data. Production code may not.

Templates: [references/templates.md](references/templates.md).

## Running

```bash
dotnet test StarterKit.slnx                                                   # everything (Docker needed)
dotnet test src/Modules/<M>/<M>.Tests                                         # one project
dotnet test src/Modules/<M>/<M>.Tests --filter "FullyQualifiedName~CreateProductHandlerTests"
dotnet test src/Modules/<M>/<M>.Tests --filter "FullyQualifiedName~DuplicateSku_Throws409"   # one test
dotnet test src/Api.Tests                                                     # architecture guardrails
cd tests/e2e && npm ci && npx playwright test                                # browser smoke (stack running)
```

If integration or E2E tests fail before any assertion runs, check that Docker is running first.

## Verify

Run the new tests and watch them fail for the right reason (temporarily break the code or the assertion), then pass.
Run `dotnet test src/Api.Tests` too.
