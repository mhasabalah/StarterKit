using Microsoft.Extensions.DependencyInjection;
using Shared.Contracts.Mediation;
using Shared.Core.Extensions;

namespace Shared.Core.Tests.Mediation;

/// <summary>
/// The owned mediator adapter keeps the semantics the modules were written against: behaviors in registration order
/// around a lazily resolved handler, caller's token unless a behavior passes another, notifications by runtime type,
/// sequential and fail-fast, idempotent registration.
/// </summary>
public sealed class MediatorTests
{
    public sealed record Ping(string Text) : IRequest<string>;

    public sealed class PingHandler(Journal journal) : IRequestHandler<Ping, string>
    {
        public Task<string> Handle(Ping request, CancellationToken cancellationToken)
        {
            journal.Add($"handler:{cancellationToken.CanBeCanceled}");
            return Task.FromResult($"pong:{request.Text}");
        }
    }

    public sealed record Unhandled : IRequest<int>;

    public sealed class Journal
    {
        public List<string> Entries { get; } = [];
        public int HandlersCreated { get; set; }
        public void Add(string entry) => Entries.Add(entry);
    }

    public sealed class Outer<TRequest, TResponse>(Journal journal) : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
    {
        public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        {
            journal.Add("outer>");
            var response = await next();
            journal.Add("<outer");
            return response;
        }
    }

    public sealed class Inner<TRequest, TResponse>(Journal journal) : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
    {
        public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        {
            journal.Add("inner>");
            var response = await next(cancellationToken);
            journal.Add("<inner");
            return response;
        }
    }

    public sealed class ShortCircuit<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
    {
        public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("rejected before the handler");
    }

    public abstract record Happened : INotification;

    public sealed record ThingHappened : Happened;

    public sealed class FirstListener(Journal journal) : INotificationHandler<ThingHappened>
    {
        public Task Handle(ThingHappened notification, CancellationToken cancellationToken)
        {
            journal.Add("first");
            return Task.CompletedTask;
        }
    }

    public sealed class FailingListener(Journal journal) : INotificationHandler<ThingHappened>
    {
        public Task Handle(ThingHappened notification, CancellationToken cancellationToken)
        {
            journal.Add("failing");
            throw new InvalidOperationException("listener failed");
        }
    }

    private static ServiceProvider Build(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<Journal>();
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }

    private static void Handlers(IServiceCollection services)
    {
        services.AddMediatorHandlers();
        services.AddTransient<IRequestHandler<Ping, string>>(sp =>
        {
            sp.GetRequiredService<Journal>().HandlersCreated++;
            return new PingHandler(sp.GetRequiredService<Journal>());
        });
    }

    [Fact]
    public async Task Send_RunsBehaviorsInRegistrationOrder_AroundTheHandler()
    {
        await using var sp = Build(s =>
        {
            Handlers(s);
            s.AddTransient(typeof(IPipelineBehavior<,>), typeof(Outer<,>));
            s.AddTransient(typeof(IPipelineBehavior<,>), typeof(Inner<,>));
        });
        using var cts = new CancellationTokenSource();

        var response = await sp.GetRequiredService<ISender>().Send(new Ping("x"), cts.Token);

        response.Should().Be("pong:x");
        sp.GetRequiredService<Journal>().Entries.Should()
            .Equal("outer>", "inner>", "handler:True", "<inner", "<outer");
    }

    [Fact]
    public async Task Send_RejectedByABehavior_NeverConstructsTheHandler()
    {
        await using var sp = Build(s =>
        {
            Handlers(s);
            s.AddTransient(typeof(IPipelineBehavior<,>), typeof(ShortCircuit<,>));
        });

        var act = () => sp.GetRequiredService<ISender>().Send(new Ping("x"));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("rejected*");
        sp.GetRequiredService<Journal>().HandlersCreated.Should().Be(0);
    }

    [Fact]
    public async Task Send_WithoutAHandler_Throws()
    {
        await using var sp = Build(s => s.AddMediatorHandlers());

        var act = () => sp.GetRequiredService<ISender>().Send(new Unhandled());

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage($"*{nameof(Unhandled)}*");
    }

    [Fact]
    public async Task Publish_ReachesHandlersOfTheRuntimeType_InOrder_AndStopsAtTheFirstFailure()
    {
        await using var sp = Build(s =>
        {
            s.AddMediatorHandlers();
            s.AddTransient<INotificationHandler<ThingHappened>, FirstListener>();
            s.AddTransient<INotificationHandler<ThingHappened>, FailingListener>();
            s.AddTransient<INotificationHandler<ThingHappened>, FirstListener>();
        });
        Happened statically = new ThingHappened();

        var act = () => sp.GetRequiredService<IPublisher>().Publish((object)statically);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("listener failed");
        sp.GetRequiredService<Journal>().Entries.Should().Equal("first", "failing");
    }

    [Fact]
    public async Task Publish_WithoutHandlers_IsANoOp_AndANonNotificationIsRejected()
    {
        await using var sp = Build(s => s.AddMediatorHandlers());
        var publisher = sp.GetRequiredService<IPublisher>();

        await publisher.Publish(new ThingHappened());
        var act = () => publisher.Publish(new object());

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public void Scanning_RegistersHandlersOnce_AndAddMediatorIsIdempotent()
    {
        var services = new ServiceCollection();

        services.AddMediator(typeof(MediatorTests).Assembly);
        services.AddMediator(typeof(MediatorTests).Assembly);

        services.Count(d => d.ServiceType == typeof(IRequestHandler<Ping, string>)).Should().Be(1);
        services.Count(d => d.ServiceType == typeof(INotificationHandler<ThingHappened>)).Should().Be(2,
            "FirstListener and FailingListener, each once");
        services.Count(d => d.ServiceType == typeof(IPipelineBehavior<,>)).Should().Be(4,
            "validation, concurrency, logging, domain-event dispatch - once each");
        services.Count(d => d.ServiceType == typeof(ISender)).Should().Be(1);
    }

    [Fact]
    public void Unit_IsASingleValue() =>
        (Unit.Value == default(Unit) && Unit.Task.Result == Unit.Value).Should().BeTrue();
}
