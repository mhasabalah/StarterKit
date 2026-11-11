# 0002. Carter minimal APIs instead of MVC controllers

Carter minimal APIs keep HTTP routing and metadata beside each vertical slice without introducing MVC controllers.

## Status

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

- Each slice is self-contained; API version metadata uses `HasApiVersion`.
- Conventions (auth, rate limit, `Produces`) are explicit per endpoint and repetitive. That repetition is
  deliberate: `RequireAuthorizationByDefault()` adds authenticated-user authorization to Carter endpoints that
  declare none, but this group-level safety net does not replace an explicit declaration. The `add-feature` skill
  keeps endpoint declarations consistent, and the architecture test rejects missing declarations.
- **Enforced by:** `NamingConventionTests` (`EndPoint`/`Module` suffixes) and `AdminEndpointAuthorizationTests`.

## Alternatives considered

- **MVC controllers:** familiar, but they work against slice locality.
- **Plain `MapGet` in `Program.cs`:** doesn't scale across modules.

## Related

[ApiGuidelines](../Architecture/ApiGuidelines.md), [Endpoint Authorization](../Reference/Endpoint-Authorization.md).
