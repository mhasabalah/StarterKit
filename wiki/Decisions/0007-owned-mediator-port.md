# 0007. Own the mediator port; do not depend on MediatR

## Status

- **Status:** Accepted
- **Date:** 2026-11-04

## Context

Every module dispatched commands and queries and published domain events through MediatR 12.5. That version is
Apache-2.0, but it is the last free major: 13+ is commercial and 12.x gets no further fixes. MediatR's types were
everywhere:

- 13 global usings
- `ICommand`/`IQuery` inheriting `MediatR.IRequest`
- `IDomainEvent : MediatR.INotification`
- about 220 files injecting `ISender`

Changing the mechanism later would have touched every module and every contract.

We use only a small subset of MediatR: send to one handler through ordered pipeline behaviors, and publish to all
handlers of a notification. We use no streams, no pre/post processors and no exception handlers.

## Decision

- **The port is ours.** `Shared.Contracts.Mediation` defines:
  - `IRequest<T>`, `IRequestHandler<TReq,TRes>`
  - `INotification`, `INotificationHandler<T>`
  - `ISender`, `IPublisher`
  - `IPipelineBehavior<TReq,TRes>`, `RequestHandlerDelegate<T>`, `Unit`

  The names deliberately match the old ones, so feature code and contracts did not change. Only the global usings
  did. `ICommand`/`IQuery`/`ICommandHandler`/`IQueryHandler` (`Shared.Contracts.CQRS`) build on these types.
- **One adapter.** `Shared.Core.Mediation.Mediator` (about 100 lines) implements `ISender` and `IPublisher` with
  plain DI. It keeps the semantics the code was written against:
  - The handler is resolved lazily inside the pipeline, so a validation failure never constructs it.
  - Behaviors run in registration order; the first registered is the outermost.
  - `next()` without a token keeps the caller's token.
  - Publish dispatches on the runtime type of the notification and runs the handlers sequentially, stopping at the
    first exception.
- **Registration.**
  - `AddMediator(assemblies)` registers handlers, the standard behaviors and validators. It is idempotent and
    replaces `AddMediatR`.
  - `AddMediatorHandlers(assemblies)` registers handlers only.
- The OpenTelemetry metric keeps its name `mediatr.request.duration`, so dashboards and alerts keep working.

## Consequences

- MediatR is gone from the dependency graph. Moving to another mechanism (a source-generated mediator such as
  `Mediator`, Wolverine, or a licensed MediatR) means writing a new `ISender`/`IPublisher` adapter in `Shared.Core`.
  Modules are untouched.
- Test fakes implement one `Send` method instead of five members.
- We own about 100 lines plus their tests (`Shared.Core.Tests/Mediation/MediatorTests.cs`).
- Enforced by `Api.Tests/Architecture/ThirdPartyIsolationTests.Nothing_references_MediatR`.
- **Verified against MediatR 12.5** (2026-11-04, Release build, 1M operations, same handler and 4 pipeline behaviors):

  | Operation | Owned mediator | MediatR 12.5 |
  |---|---|---|
  | `Send` | about 190–225 ns, 920 B | about 210–230 ns, 928 B |
  | `Publish` to 2 handlers | about 75 ns, 168 B | about 130 ns, 496 B |

  1.6M concurrent sends returned no wrong results. Both run under `ValidateScopes`.
- Notifications are dispatched by their **exact runtime type**. A handler for a base class or interface never runs,
  and the same was true of MediatR 12 on Microsoft DI.
  `ThirdPartyIsolationTests.Notification_handlers_target_concrete_event_types` turns that silent no-op into a build
  failure.
- Not AOT/trimming-safe: it uses `MakeGenericType`, as MediatR did. Moving to a source-generated adapter is the path
  if Native AOT ever becomes a requirement.

## Alternatives considered

- **Stay on MediatR 12.5.** Free forever, but frozen. Every module would stay coupled to a vendor namespace, and the
  same rewrite would come later under time pressure.
- **Buy MediatR 13+.** It costs money every year for a feature set we use about 5% of.
- **An adapter that keeps MediatR underneath.** Our contracts must not inherit vendor interfaces, so this needs
  envelope types and reflection to bridge them. That is more code than the dispatcher itself, for no gain.
- **A source-generated mediator now.** It is faster, but it brings its own interfaces and analyzers. The owned port
  keeps that option open without paying for it today.

## Related

[DesignPatterns](../Architecture/DesignPatterns.md), [Architecture Guardrails](../Reference/Architecture-Guardrails.md).
