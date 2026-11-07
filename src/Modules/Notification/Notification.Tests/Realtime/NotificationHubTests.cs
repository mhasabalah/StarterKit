using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Notification.Contracts.Inbox;
using Notification.Contracts.Realtime;
using Testcontainers.Redis;

namespace Notification.Tests.Realtime;

/// <summary>
/// The real hub over a real (in-process) server and the real .NET SignalR client: user targeting, isolation between
/// users, authentication, and best-effort sends. Host authentication (JWT, blacklist, query-string token) is covered
/// end to end in <c>Api.E2ETests</c>.
/// </summary>
public sealed class NotificationHubTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private TestServer _server = null!;

    public async Task InitializeAsync() => (_app, _server) = await StartHostAsync(redisConnection: null);

    public async Task DisposeAsync() => await _app.DisposeAsync();

    internal static async Task<(WebApplication App, TestServer Server)> StartHostAsync(string? redisConnection,
        ILoggerProvider? logs = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        if (logs is not null)
            builder.Logging.AddProvider(logs).SetMinimumLevel(LogLevel.Debug);
        builder.Services.AddAuthentication(HeaderAuthHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, HeaderAuthHandler>(HeaderAuthHandler.SchemeName, _ => { });
        builder.Services.AddAuthorization();
        var signalR = builder.Services.AddSignalR();
        if (redisConnection is not null)
            signalR.AddStackExchangeRedis(redisConnection, o => o.Configuration.ChannelPrefix = StackExchange.Redis.RedisChannel.Literal("test"));
        builder.Services.AddSingleton<IUserIdProvider, NotificationHubUserIdProvider>();
        builder.Services.AddSingleton<NotificationMetrics>();
        builder.Services.AddSingleton<HubConnectionRegistry>();
        builder.Services.AddSingleton<HubSessionCloser>();
        builder.Services.AddSingleton<IHubRevocationBroadcaster, LocalHubRevocationBroadcaster>();
        builder.Services.AddSingleton<IRealtimeNotifier, SignalRRealtimeNotifier>();
        builder.Services.AddSingleton<IUserEventPublisher, SignalRUserEventPublisher>();

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapHub<NotificationHub>(NotificationHubContract.Path).RequireAuthorization();
        await app.StartAsync();
        return (app, app.GetTestServer());
    }

    internal static HubConnection Connect(TestServer server, Guid? userId, HttpTransportType transport = HttpTransportType.LongPolling, Guid? sessionId = null) =>
        new HubConnectionBuilder()
            .WithUrl(new Uri(server.BaseAddress, NotificationHubContract.Path.TrimStart('/')), o =>
            {
                o.Transports = transport;
                o.HttpMessageHandlerFactory = _ => server.CreateHandler();
                if (transport == HttpTransportType.WebSockets)
                {
                    o.SkipNegotiation = true;
                    o.WebSocketFactory = async (context, ct) =>
                    {
                        var ws = server.CreateWebSocketClient();
                        if (userId is { } id)
                            ws.ConfigureRequest = r =>
                            {
                                r.Headers[HeaderAuthHandler.Header] = id.ToString();
                                if (sessionId is { } sidWs)
                                    r.Headers[HeaderAuthHandler.SidHeader] = sidWs.ToString();
                            };
                        return await ws.ConnectAsync(context.Uri, ct);
                    };
                }

                if (userId is { } user)
                    o.Headers[HeaderAuthHandler.Header] = user.ToString();
                if (sessionId is { } sid)
                    o.Headers[HeaderAuthHandler.SidHeader] = sid.ToString();
            })
            .Build();

    private static InboxNotificationDto Dto(string title) =>
        new(Guid.NewGuid(), Guid.NewGuid(), null, "test", NotificationSeverity.Info, title, "body", null, null, DateTime.UtcNow, null);

    [Fact]
    public async Task Every_session_of_the_user_gets_the_event_and_other_users_get_nothing()
    {
        Guid alice = Guid.NewGuid(), bob = Guid.NewGuid();
        var aliceTab1 = new TaskCompletionSource<InboxNotificationDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        var aliceTab2 = new TaskCompletionSource<InboxNotificationDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        var bobGot = 0;

        await using var tab1 = Connect(_server, alice);
        await using var tab2 = Connect(_server, alice, HttpTransportType.WebSockets);
        await using var bobs = Connect(_server, bob);
        tab1.On<InboxNotificationDto>(NotificationHubContract.NotificationReceived, n => aliceTab1.TrySetResult(n));
        tab2.On<InboxNotificationDto>(NotificationHubContract.NotificationReceived, n => aliceTab2.TrySetResult(n));
        bobs.On<InboxNotificationDto>(NotificationHubContract.NotificationReceived, _ => Interlocked.Increment(ref bobGot));
        await Task.WhenAll(tab1.StartAsync(), tab2.StartAsync(), bobs.StartAsync());

        var notifier = _app.Services.GetRequiredService<IRealtimeNotifier>();
        var dto = Dto("for alice");
        await notifier.NotificationReceivedAsync(alice, dto);

        (await aliceTab1.Task.WaitAsync(TimeSpan.FromSeconds(10))).Id.Should().Be(dto.Id);
        (await aliceTab2.Task.WaitAsync(TimeSpan.FromSeconds(10))).Title.Should().Be("for alice");
        await Task.Delay(300);
        bobGot.Should().Be(0);
    }

    [Fact]
    public async Task Unread_count_read_and_delete_events_reach_the_client_with_their_payload()
    {
        var user = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var count = new TaskCompletionSource<UnreadCountChangedEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        var read = new TaskCompletionSource<NotificationsReadEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        var deleted = new TaskCompletionSource<NotificationDeletedEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var client = Connect(_server, user);
        client.On<UnreadCountChangedEvent>(NotificationHubContract.UnreadCountChanged, e => count.TrySetResult(e));
        client.On<NotificationsReadEvent>(NotificationHubContract.NotificationsRead, e => read.TrySetResult(e));
        client.On<NotificationDeletedEvent>(NotificationHubContract.NotificationDeleted, e => deleted.TrySetResult(e));
        await client.StartAsync();
        var notifier = _app.Services.GetRequiredService<IRealtimeNotifier>();
        var id = Guid.NewGuid();

        await notifier.UnreadCountChangedAsync(user, tenant, 7);
        await notifier.NotificationsReadAsync(user, new NotificationsReadEvent([id], DateTime.UtcNow, null));
        await notifier.NotificationDeletedAsync(user, id);

        (await count.Task.WaitAsync(TimeSpan.FromSeconds(10))).Should().Be(new UnreadCountChangedEvent(tenant, 7));
        (await read.Task.WaitAsync(TimeSpan.FromSeconds(10))).Ids.Should().Equal(id);
        (await deleted.Task.WaitAsync(TimeSpan.FromSeconds(10))).Id.Should().Be(id);
    }

    [Fact]
    public async Task An_unauthenticated_client_cannot_connect()
    {
        await using var anonymous = Connect(_server, userId: null);

        await anonymous.Invoking(c => c.StartAsync()).Should().ThrowAsync<HttpRequestException>()
            .Where(e => e.StatusCode == System.Net.HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task The_hub_accepts_no_client_invocations()
    {
        await using var client = Connect(_server, Guid.NewGuid());
        await client.StartAsync();

        await client.Invoking(c => c.InvokeAsync("NotificationReceived", Dto("spoof")))
            .Should().ThrowAsync<HubException>("the hub is push-only; a client cannot send events to anyone");
    }

    [Fact]
    public async Task A_failing_send_is_swallowed_and_never_fails_the_caller()
    {
        var hub = new Mock<IHubContext<NotificationHub, INotificationClient>>();
        hub.SetupGet(h => h.Clients).Throws(new InvalidOperationException("redis down"));
        var notifier = new SignalRRealtimeNotifier(hub.Object, new NotificationMetrics(), NullLogger<SignalRRealtimeNotifier>.Instance);

        await notifier.Invoking(n => n.NotificationReceivedAsync(Guid.NewGuid(), Dto("x"))).Should().NotThrowAsync();
        await notifier.Invoking(n => n.UnreadCountChangedAsync(Guid.NewGuid(), null, 1)).Should().NotThrowAsync();
    }

    [Fact]
    public async Task A_user_event_reaches_only_that_users_sessions_with_its_payload()
    {
        var userId = Guid.NewGuid();
        await using var mine = Connect(_server, userId);
        await using var other = Connect(_server, Guid.NewGuid());
        var received = new TaskCompletionSource<UserEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        var leaked = false;
        mine.On<UserEvent>(NotificationHubContract.UserEvent, e => received.TrySetResult(e));
        other.On<UserEvent>(NotificationHubContract.UserEvent, _ => leaked = true);
        await mine.StartAsync();
        await other.StartAsync();

        var publisher = _app.Services.GetRequiredService<IUserEventPublisher>();
        var jobId = Guid.NewGuid();
        await publisher.PublishAsync(userId, new UserEvent("datatransfer.job-progress", null,
            new Dictionary<string, object?> { ["jobId"] = jobId, ["processedRows"] = 1500 }));

        var e = await received.Task.WaitAsync(TimeSpan.FromSeconds(10));
        e.Type.Should().Be("datatransfer.job-progress");
        e.Data["jobId"]!.ToString().Should().Be(jobId.ToString());
        e.Data["processedRows"]!.ToString().Should().Be("1500");
        leaked.Should().BeFalse("events are addressed by user id");
    }

    [Fact]
    public async Task A_user_event_outside_the_contract_limits_is_dropped_and_a_failing_send_never_throws()
    {
        var hub = new Mock<IHubContext<NotificationHub, INotificationClient>>();
        hub.SetupGet(h => h.Clients).Throws(new InvalidOperationException("redis down"));
        var publisher = new SignalRUserEventPublisher(hub.Object, new NotificationMetrics(),
            NullLogger<SignalRUserEventPublisher>.Instance);

        var tooBig = Enumerable.Range(0, UserEvent.MaxDataEntries + 1).ToDictionary(i => $"k{i}", i => (object?)i);
        await publisher.Invoking(p => p.PublishAsync(Guid.NewGuid(), new UserEvent("x.y", null, tooBig))).Should().NotThrowAsync();
        await publisher.Invoking(p => p.PublishAsync(Guid.NewGuid(), new UserEvent("x.y", null, new Dictionary<string, object?>())))
            .Should().NotThrowAsync();
        hub.VerifyGet(h => h.Clients, Times.Once(), "the oversized event was dropped before any send");
    }

    [Fact]
    public void The_user_id_is_the_canonical_guid_of_the_name_identifier_claim()
    {
        var id = Guid.NewGuid();
        var transport = new Microsoft.AspNetCore.Connections.DefaultConnectionContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id.ToString("B").ToUpperInvariant())], "test"))
        };
        var connection = new HubConnectionContext(transport, new HubConnectionContextOptions(), NullLoggerFactory.Instance);

        new NotificationHubUserIdProvider().GetUserId(connection).Should().Be(id.ToString("D"));
    }

    /// <summary>Authenticates <c>X-Test-User: &lt;guid&gt;</c> as that user.</summary>
    internal sealed class HeaderAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "TestHeader";
        public const string Header = "X-Test-User";
        public const string SidHeader = "X-Test-Sid";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(Header, out var value) || !Guid.TryParse(value, out var id))
                return Task.FromResult(AuthenticateResult.NoResult());
            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, id.ToString()) };
            if (Request.Headers.TryGetValue(SidHeader, out var sid))
                claims.Add(new Claim("sid", sid.ToString()));
            var identity = new ClaimsIdentity(claims, SchemeName);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }
}

/// <summary>
/// Scale-out: two API replicas share a Redis backplane; a send on replica A (where the Hangfire job ran) reaches a
/// socket held by replica B.
/// </summary>
public sealed class NotificationHubRedisBackplaneTests(Xunit.Abstractions.ITestOutputHelper output) : IAsyncLifetime
{
    private readonly RedisContainer _redis = new RedisBuilder("redis:7-alpine").Build();
    private readonly ListLoggerProvider _logs = new();
    private WebApplication _replicaA = null!;
    private WebApplication _replicaB = null!;

    public async Task InitializeAsync()
    {
        await _redis.StartAsync();
        (_replicaA, _) = await NotificationHubTests.StartHostAsync(_redis.GetConnectionString(), _logs);
        (_replicaB, _) = await NotificationHubTests.StartHostAsync(_redis.GetConnectionString(), _logs);
    }

    public async Task DisposeAsync()
    {
        await _replicaA.DisposeAsync();
        await _replicaB.DisposeAsync();
        await _redis.DisposeAsync();
    }

    [Fact]
    public async Task A_send_on_one_replica_reaches_the_users_socket_on_another()
    {
        var user = Guid.NewGuid();
        var received = new TaskCompletionSource<UnreadCountChangedEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var client = NotificationHubTests.Connect(_replicaB.GetTestServer(), user);
        client.On<UnreadCountChangedEvent>(NotificationHubContract.UnreadCountChanged, e => received.TrySetResult(e));
        await client.StartAsync();

        // Hub events are best effort (clients resync over REST), and a cold replica connects to Redis lazily on its
        // first send: re-send until it lands, as a client-visible guarantee "it crosses replicas", not "first try".
        var notifier = _replicaA.Services.GetRequiredService<IRealtimeNotifier>();
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (!received.Task.IsCompleted && DateTime.UtcNow < deadline)
        {
            await notifier.UnreadCountChangedAsync(user, null, 3);
            await Task.WhenAny(received.Task, Task.Delay(500));
        }

        try
        {
            received.Task.IsCompleted.Should().BeTrue("a send on replica A must reach the socket held by replica B");
            (await received.Task).Count.Should().Be(3);
        }
        finally
        {
            foreach (var line in _logs.Lines.Where(l => l.Contains("Redis", StringComparison.OrdinalIgnoreCase) || l.StartsWith("Warn") || l.StartsWith("Err")))
                output.WriteLine(line);
        }
    }

    private sealed class ListLoggerProvider : ILoggerProvider
    {
        public System.Collections.Concurrent.ConcurrentQueue<string> Lines { get; } = new();

        public ILogger CreateLogger(string categoryName) => new L(this, categoryName);

        public void Dispose() { }

        private sealed class L(ListLoggerProvider p, string c) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                p.Lines.Enqueue($"{logLevel} {c}: {formatter(state, exception)} {exception?.GetType().Name} {exception?.Message}");
        }
    }
}
