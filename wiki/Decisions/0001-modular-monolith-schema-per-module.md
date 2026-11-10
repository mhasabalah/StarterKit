# 0001. Modular monolith with one schema and DbContext per module

## Status

- **Status:** Accepted
- **Date:** 2026-11-04 (recorded; the decision predates the ADR log)

## Context

A starter must be cheap to run (one deployable, one database) and still let a product grow into clear bounded
contexts, some of which may later be extracted into separate services.

## Decision

- One ASP.NET Core host, with business capabilities as modules under `src/Modules/<M>`.
- Each module owns a `DbContext`, a Postgres **schema**, its migrations and a `.Contracts` project.
- Modules communicate only through `ISender` + `.Contracts` (sync) or integration events through the outbox (async).
- There are no cross-schema foreign keys or joins. Other modules' ids are stored as plain values.

## Consequences

- One database to operate and back up, with clear ownership per schema, and modules that can be extracted later
  (`.Contracts` becomes the API).
- Some data is duplicated or projected across modules, and reports that span modules go through queries or events.
- **Enforced by:** `ModuleBoundaryTests`, `MessagingArchitectureTests` (Contracts stay thin), and the skills
  `new-module` and `add-integration-event`.

## Alternatives considered

- **Microservices from day one:** operational cost far too high for a starter.
- **A single shared DbContext:** fast at first, but every module becomes coupled to every table.

## Related

[Overview](../Architecture/Overview.md), [DatabaseConventions](../Architecture/DatabaseConventions.md).
