# 0002. Carter minimal APIs instead of MVC controllers

- **Status:** Accepted
- **Date:** 2026-11-04 (recorded)

## Context

Vertical slices put each use case's endpoint, validator and handler side by side. Controllers group by resource and
pull in MVC infrastructure that slices don't need.

## Decision

Endpoints are Carter `ICarterModule` classes: one `<Feature>EndPoint` per slice, or a grouped `<X>sModule` for small
lookups. They are discovered per module with `AddCarterWithAssemblies`. Endpoints map request → command/query, call
`ISender`, and wrap results in `ApiResponse`.

## Consequences

- Each slice is self-contained, has little ceremony, and is easy to version (`HasApiVersion`).
- Conventions (auth, rate limit, `Produces`) are explicit per endpoint and repetitive. That repetition is
  deliberate: there is no global fallback auth policy (see Template-Backlog #23), and the `add-feature` skill keeps
  it consistent.
- **Enforced by:** `NamingConventionTests` (`EndPoint`/`Module` suffixes) and `AdminEndpointAuthorizationTests`.

## Alternatives considered

- **MVC controllers:** familiar, but they work against slice locality.
- **Plain `MapGet` in `Program.cs`:** doesn't scale across modules.
