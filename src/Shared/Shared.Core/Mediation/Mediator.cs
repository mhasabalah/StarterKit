using System.Collections.Concurrent;

namespace Shared.Core.Mediation;

/// <summary>
/// The default (and only) adapter behind <see cref="ISender"/> / <see cref="IPublisher"/>: a plain DI-based dispatcher.
/// Semantics match what the modules relied on before (MediatR 12): the handler is resolved lazily INSIDE the pipeline,
/// behaviors wrap it in registration order (first registered = outermost), a behavior calling <c>next()</c> without a
/// token keeps the caller's token, and notifications go to every handler of the notification's runtime type,
/// sequentially, stopping at the first exception.
/// </summary>
internal sealed class Mediator(IServiceProvider serviceProvider) : ISender, IPublisher
{
    private static readonly ConcurrentDictionary<(Type Request, Type Response), object> RequestDispatchers = new();
    private static readonly ConcurrentDictionary<Type, NotificationDispatcher> NotificationDispatchers = new();

    public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var dispatcher = (RequestDispatcher<TResponse>)RequestDispatchers.GetOrAdd(
            (request.GetType(), typeof(TResponse)),
            static key => Activator.CreateInstance(
                typeof(RequestDispatcher<,>).MakeGenericType(key.Request, key.Response))!);

        return dispatcher.Dispatch(request, serviceProvider, cancellationToken);
    }

    public Task Publish(object notification, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notification);
        if (notification is not INotification typed)
            throw new ArgumentException($"{notification.GetType()} does not implement {nameof(INotification)}.", nameof(notification));

        return PublishCore(typed, cancellationToken);
    }

    public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification
    {
        ArgumentNullException.ThrowIfNull(notification);
        return PublishCore(notification, cancellationToken);
    }

    private Task PublishCore(INotification notification, CancellationToken cancellationToken) =>
        NotificationDispatchers.GetOrAdd(
                notification.GetType(),
                static type => (NotificationDispatcher)Activator.CreateInstance(
                    typeof(NotificationDispatcher<>).MakeGenericType(type))!)
            .Dispatch(notification, serviceProvider, cancellationToken);

    private abstract class RequestDispatcher<TResponse>
    {
        public abstract Task<TResponse> Dispatch(
            IRequest<TResponse> request, IServiceProvider services, CancellationToken cancellationToken);
    }

    private sealed class RequestDispatcher<TRequest, TResponse> : RequestDispatcher<TResponse>
        where TRequest : IRequest<TResponse>
    {
        public override Task<TResponse> Dispatch(
            IRequest<TResponse> request, IServiceProvider services, CancellationToken cancellationToken)
        {
            var typed = (TRequest)request;

            RequestHandlerDelegate<TResponse> pipeline = token =>
                services.GetRequiredService<IRequestHandler<TRequest, TResponse>>()
                    .Handle(typed, token == default ? cancellationToken : token);

            foreach (var behavior in services.GetServices<IPipelineBehavior<TRequest, TResponse>>().Reverse())
            {
                var next = pipeline;
                pipeline = token => behavior.Handle(typed, next, token == default ? cancellationToken : token);
            }

            return pipeline(cancellationToken);
        }
    }

    private abstract class NotificationDispatcher
    {
        public abstract Task Dispatch(INotification notification, IServiceProvider services, CancellationToken cancellationToken);
    }

    private sealed class NotificationDispatcher<TNotification> : NotificationDispatcher
        where TNotification : INotification
    {
        public override async Task Dispatch(
            INotification notification, IServiceProvider services, CancellationToken cancellationToken)
        {
            foreach (var handler in services.GetServices<INotificationHandler<TNotification>>())
                await handler.Handle((TNotification)notification, cancellationToken);
        }
    }
}
