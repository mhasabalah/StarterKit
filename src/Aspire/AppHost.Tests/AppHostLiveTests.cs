using System.Runtime.CompilerServices;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using AppHost;

namespace AppHost.Tests;

/// <summary>Runs only when <c>ASPIRE_LIVE_TESTS=1</c> (needs Docker; pulls images on first run).</summary>
public sealed class LiveFactAttribute : FactAttribute
{
    public LiveFactAttribute([CallerFilePath] string? sourceFilePath = null, [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber) => Skip = LiveTests.SkipReason;
}

/// <inheritdoc cref="LiveFactAttribute"/>
public sealed class LiveTheoryAttribute : TheoryAttribute
{
    public LiveTheoryAttribute([CallerFilePath] string? sourceFilePath = null, [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber) => Skip = LiveTests.SkipReason;
}

internal static class LiveTests
{
    public const string Variable = "ASPIRE_LIVE_TESTS";

    public static string? SkipReason => Environment.GetEnvironmentVariable(Variable) == "1"
        ? null
        : $"Live AppHost test: set {Variable}=1 (requires Docker).";
}

/// <summary>
/// Starts the real AppHost (Postgres, Redis, Seq, API, broker) and checks the API end to end. Readiness covers Postgres,
/// Redis and the MassTransit bus, so a healthy API proves that the connection strings Aspire injects are the ones the
/// API actually uses. The testing host randomizes ports, so these tests can run next to a developer's AppHost.
/// </summary>
public class AppHostLiveTests
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(5);

    private static async Task<DistributedApplication> StartAsync(CancellationToken ct, params string[] args)
    {
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>(args, ct);
        var app = await builder.BuildAsync(ct);
        await app.StartAsync(ct);
        await app.ResourceNotifications.WaitForResourceHealthyAsync("api", ct);
        return app;
    }

    [LiveFact]
    public async Task Api_becomes_healthy_and_serves_requests()
    {
        using var cts = new CancellationTokenSource(StartupTimeout);
        await using var app = await StartAsync(cts.Token);
        using var client = app.CreateHttpClient("api");

        var health = await client.GetAsync("/health", cts.Token);
        health.StatusCode.Should().Be(HttpStatusCode.OK, await health.Content.ReadAsStringAsync(cts.Token));

        (await client.GetAsync("/alive", cts.Token)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/", cts.Token)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [LiveTheory]
    [InlineData(MessagingTransport.InMemory)]
    [InlineData(MessagingTransport.PostgreSql)]
    [InlineData(MessagingTransport.RabbitMq)]
    public async Task Api_is_healthy_on_every_message_transport(MessagingTransport transport)
    {
        using var cts = new CancellationTokenSource(StartupTimeout);
        await using var app = await StartAsync(cts.Token, $"{AppHostSettings.TransportKey}={transport}");
        using var client = app.CreateHttpClient("api");

        // /health includes the MassTransit bus check: healthy = the bus started and is connected to the transport.
        var health = await client.GetAsync("/health", cts.Token);
        health.StatusCode.Should().Be(HttpStatusCode.OK, await health.Content.ReadAsStringAsync(cts.Token));
    }

    [LiveFact]
    public async Task RabbitMq_transport_creates_the_api_queues_on_the_broker()
    {
        using var cts = new CancellationTokenSource(StartupTimeout);
        await using var app = await StartAsync(cts.Token, $"{AppHostSettings.TransportKey}=RabbitMq");

        // Same credentials the API received (ConnectionStrings:rabbitmq = amqp://user:password@host:port).
        var amqp = new Uri((await app.GetConnectionStringAsync("rabbitmq", cts.Token))!);
        var credentials = Uri.UnescapeDataString(amqp.UserInfo);

        using var management = new HttpClient { BaseAddress = app.GetEndpoint("rabbitmq", "management") };
        management.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(credentials)));

        // Queues are declared when the bus starts its receive endpoints; poll briefly instead of assuming timing.
        string[] queues = [];
        for (var attempt = 0; attempt < 20 && queues.Length == 0; attempt++)
        {
            var response = await management.GetFromJsonAsync<RabbitQueue[]>("/api/queues", cts.Token);
            queues = response?.Select(q => q.Name).ToArray() ?? [];
            if (queues.Length == 0)
                await Task.Delay(TimeSpan.FromSeconds(1), cts.Token);
        }

        queues.Should().NotBeEmpty("the API's consumers declare their receive endpoints on the broker");
    }

    private sealed record RabbitQueue(string Name);
}
