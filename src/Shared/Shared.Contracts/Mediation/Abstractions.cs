namespace Shared.Contracts.Mediation;

// The in-process request/notification PORT. Feature code depends only on these types; the single implementation is
// Shared.Core.Mediation.Mediator. Replacing the mechanism (a source-generated mediator, Wolverine, a commercial
// MediatR...) means a new adapter behind ISender/IPublisher, not a change to the modules. See
// wiki/Decisions/0007-owned-mediator-port.md.

/// <summary>Marker for anything <see cref="ISender"/> can dispatch.</summary>
public interface IBaseRequest;

/// <summary>A request with exactly one handler that returns <typeparamref name="TResponse"/>.</summary>
public interface IRequest<out TResponse> : IBaseRequest;

/// <summary>Handles <typeparamref name="TRequest"/>. Prefer <c>ICommandHandler</c>/<c>IQueryHandler</c> in feature code.</summary>
public interface IRequestHandler<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    Task<TResponse> Handle(TRequest request, CancellationToken cancellationToken);
}

/// <summary>The rest of the pipeline. Passing no token keeps the caller's token.</summary>
public delegate Task<TResponse> RequestHandlerDelegate<TResponse>(CancellationToken cancellationToken = default);

/// <summary>
/// Cross-cutting step around every request (validation, concurrency, logging, telemetry...). Behaviors run in
/// registration order: the first registered is the outermost.
/// </summary>
public interface IPipelineBehavior<in TRequest, TResponse>
    where TRequest : notnull
{
    Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken);
}

/// <summary>An in-process fact with zero or more handlers (domain events implement it).</summary>
public interface INotification;

/// <summary>Handles <typeparamref name="TNotification"/>. Handlers run one after another; an exception stops the rest.</summary>
public interface INotificationHandler<in TNotification>
    where TNotification : INotification
{
    Task Handle(TNotification notification, CancellationToken cancellationToken);
}

/// <summary>Dispatches a request to its single handler through the pipeline behaviors.</summary>
public interface ISender
{
    Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default);
}

/// <summary>Publishes a notification to every handler of its RUNTIME type.</summary>
public interface IPublisher
{
    Task Publish(object notification, CancellationToken cancellationToken = default);

    Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification;
}

/// <summary>The "void" response of commands that return nothing.</summary>
public readonly record struct Unit
{
    public static readonly Unit Value;

    public static Task<Unit> Task { get; } = System.Threading.Tasks.Task.FromResult(Value);

    public override string ToString() => "()";
}
