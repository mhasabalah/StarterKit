---
name: review
description: Senior code review of StarterKit changes or the whole codebase - interactive, section by section (Architecture, Code Quality, Tests, Performance), numbered issues with lettered options (effort/risk/impact/maintenance) and an opinionated recommendation, checked against the project's architecture guardrails. Use for "review the codebase", "review my changes", "architecture review", "code review this module".
---

# StarterKit code review

## Scope

Ask what to review if it isn't clear: the working-tree diff (`git diff`, `git diff --staged`), a module, a path, or
the whole codebase. For a diff, first run the guardrail pass below. Optionally delegate it to the
`starterkit:architecture-reviewer` agent and then review its findings.

## Engineering preferences (every recommendation maps to these)

- DRY is important; flag repetition aggressively.
- Well-tested code is non-negotiable; err toward more tests.
- "Engineered enough": not fragile or hacky, not over-abstracted.
- Handle more edge cases, not fewer; thoughtfulness over speed.
- Explicit over clever.

## Interaction rules

1. **Before starting**, ask with `AskUserQuestion`: **BIG CHANGE** (up to 4 issues per section, one section at a
   time) or **SMALL CHANGE** (1 issue per section).
2. Go through the sections in order:
   1. **Architecture**: system design, component boundaries, dependency graph, coupling, data flow, scaling,
      security (auth, data access, API boundaries, tenant isolation).
   2. **Code Quality**: organization, DRY violations, error-handling gaps, technical debt, over- or under-engineering.
   3. **Tests**: coverage gaps (unit, integration, E2E), assertion strength, missing edge cases, untested failure modes.
   4. **Performance**: N+1 queries, missing `AsNoTracking` or projections, unbounded queries, memory, caching
      opportunities, high-complexity paths.
3. **Per issue**:
   - Number it (Issue 1, Issue 2, …) and describe the problem with `file:line` references.
   - Present 2-3 lettered options (A, B, C), including "do nothing" where reasonable. The recommended option is
     always listed **first**.
   - For each option, state **Effort** | **Risk** | **Impact** | **Maintenance burden**.
   - Give a clear recommendation and say which engineering preference it serves.
4. After each section, call `AskUserQuestion` and don't proceed without explicit confirmation.
5. Don't assume priorities on timeline or scale.
6. Don't edit code during the review. If the user approves fixes, apply them afterwards, run
   `dotnet build StarterKit.slnx` and the relevant tests, and never commit.

## Guardrail pass

Each line is a rule, then the place that enforces it.

- **Module boundaries:** no `ProjectReference` or `using` of another module's implementation; cross-module calls go
  only through `ISender` + `.Contracts`; users come only through `UserDirectory.Contracts`.
  Enforced by `ModuleBoundaryTests`.
- **Contracts stay thin:** `.Contracts` reference no `Shared.Core` or `Shared.Messaging` (the Tenancy.Contracts
  exception is whitelisted). Enforced by `MessagingArchitectureTests`.
- **Tenancy:** tenant entities are `IMustHaveTenant`; there is no parameterless `IgnoreQueryFilters()` in
  production code; unique indexes are tenant-first; `TenantId` is never set by hand; cross-tenant work uses
  `TenantScope.BeginHostScope()`, small and explicit. Enforced by `TenantIsolationArchitectureTests`.
- **Outbox:** cross-module facts use `AddIntegrationEvent`, never `IPublishEndpoint.Publish` from features.
  Consumers are idempotent and re-read state. Enforced by `MessagingArchitectureTests`.
- **Jobs and consumers:** DB work runs through `IUnitOfWorkRunner`, or `IDomainEventDispatcher` after saving.
  Concurrency conflicts are logged and skipped. Enforced by `BackgroundJobEventDispatchTests`.
- **Auth:** every endpoint declares auth explicitly (deny-by-default is only a safety net). Admin routes have a permission.
  Destructive actions use `.DisallowImpersonation()`. Enforced by `AdminEndpointAuthorizationTests`.
- **Naming:** suffixes Handler, Command, Query, Validator, Event, EndPoint or Module. Enforced by
  `NamingConventionTests`.
- **Child keys:** entities added through a parent's collection with domain-assigned Guid ids use
  `ValueGeneratedNever()`. Enforced by `ClientGeneratedKeyTests`.
- **RLS coverage:** every `tenant_id` table in a tenant-data schema has forced RLS. Enforced by
  `TenantRowLevelSecurityCoverageTests` (`Api.E2ETests`).
- **Concurrency:** critical updates take `long? Version`, call `ExpectVersion`, and the DTO carries `Version`.
  Nobody sets `Version` by hand. Checked in review.
- **Time:** new code uses `TimeProvider`, not `DateTime.UtcNow` in handlers or jobs. Checked in review.
- **Errors:** errors are module exceptions (NotFound, BadRequest, Conflict, Forbidden); there is no ad-hoc
  `Results.Problem`. Checked in review.
- **Localization:** new validation messages exist in both the en and ar resx files. Checked in review.
- **Migrations:** a model change has a migration; RLS is set on new tenant tables. Enforced by
  `PendingModelChangesTests`.
- **Secrets:** no real hosts, IPs or credentials; use `CHANGE_ME`, `api.example.com`, `YOUR_SERVER_IP`.
  Checked in review.
- **Docs:** wiki, CLAUDE.md and the README are updated when behaviour or structure changed. Checked in review.

To run the enforced checks: `dotnet test src/Api.Tests`.
