# StarterKit: GitHub Copilot instructions

StarterKit is an **ASP.NET Core 10 / .NET 10 / EF Core 10** modular monolith starter: Carter minimal APIs, an owned mediator (`Shared.Contracts.Mediation`),
FluentValidation, Mapster, PostgreSQL (snake_case), Redis, MassTransit with a transactional outbox, Hangfire,
OpenTelemetry, .NET Aspire, and optional multi-tenancy.

**`CLAUDE.md` at the repository root is the single source of truth** for conventions, architecture rules and
guardrails. Follow it. `AGENTS.md` lists the non-negotiable rules. Step-by-step procedures with code templates are in
`tools/claude/starterkit-plugin/skills/<name>/SKILL.md`:

| Skill | Use it for |
|---|---|
| `new-module` | modules |
| `add-feature` | endpoints and slices |
| `add-aggregate` | entities |
| `add-migration` | migrations |
| `add-integration-event` | integration events |
| `add-background-job` | jobs |
| `add-permission` | authorization |
| `add-email-template` | email |
| `write-tests` | tests |
| `review` | review |

The reference implementation for every pattern is `src/Modules/Catalog`.

Never suggest:
- referencing another module's implementation project (use `.Contracts` + `ISender`)
- the parameterless `IgnoreQueryFilters()`
- setting `TenantId` or `Version` by hand
- `IPublishEndpoint.Publish` from feature code (use `AddIntegrationEvent`)
- an endpoint without explicit authorization
- `DateTime.UtcNow` in new handlers or jobs (use `TimeProvider`)
- real hostnames, IPs or credentials
