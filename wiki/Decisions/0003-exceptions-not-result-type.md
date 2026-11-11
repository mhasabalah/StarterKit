# 0003. Typed exceptions mapped centrally, no Result type

Typed exceptions and one shared HTTP error boundary provide consistent client failures without a Result abstraction.

## Status

- **Status:** Accepted
- **Date:** 2026-11-04 (recorded)

## Context

Handlers need a uniform way to express "not found", "forbidden", "conflict" and validation failures, and clients
need one error shape.

## Decision

- Handlers and entities throw typed exceptions (`NotFoundException`, `BadRequestException`/`DomainException`,
  `ForbiddenException`, `ConflictException`, `ConcurrencyConflictException`, `GoneException`, module subclasses).
- `GlobalExceptionHandler` + `ExceptionStatusMapper` are the **only** place that maps them to status codes and to the
  `ApiResponse` error envelope (`isSuccess=false, statusCode, errors[], traceId`). A 5xx returns a generic message;
  the details go to the logs.
- Validation goes through FluentValidation in `ValidationBehavior`.

## Consequences

- Handlers stay linear and readable, and HTTP mapping lives in one place.
- Exceptions are used for expected outcomes. That's an accepted trade-off at this scale; they are cheap compared
  with I/O.
- Don't introduce a parallel `Result<T>` style. Docs and skills use exceptions only.

## Alternatives considered

- **`Result<T>` / OneOf:** explicit in signatures, but adds ceremony to every call and needs its own mapping layer.

## Related

[Errors and Responses](../Architecture/Errors-and-Responses.md).
