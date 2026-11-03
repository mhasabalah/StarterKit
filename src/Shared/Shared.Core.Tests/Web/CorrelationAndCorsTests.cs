using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging;
using Shared.Core.Extensions;
using Shared.Core.Tests.Infrastructure;
using Shared.Core.Web;

namespace Shared.Core.Tests.Web;

public sealed class CorrelationIdTests
{
    private static Task<TestApp> StartAsync() =>
        TestApp.StartAsync(null, app =>
        {
            app.UseCorrelationId();
            app.MapGet("/c", (HttpContext ctx, ILogger<CorrelationIdTests> logger) =>
            {
                logger.LogInformation("inside endpoint");
                return Results.Text(ctx.GetCorrelationId());
            });
        });

    [Theory]
    [InlineData("abc-123")]                               // not a GUID: tolerated
    [InlineData("0f8fad5b-d9cb-469f-a165-70867728950e")]
    [InlineData("Root=1-5759e988-bd862e3fe1be46a994272793")] // AWS-style trace id
    public async Task Incoming_id_is_used_and_echoed(string incoming)
    {
        await using var app = await StartAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/c");
        request.Headers.Add(CorrelationExtensions.HeaderName, incoming);

        var response = await app.Client.SendAsync(request);

        (await response.Content.ReadAsStringAsync()).Should().Be(incoming);
        response.Headers.GetValues(CorrelationExtensions.HeaderName).Should().ContainSingle().Which.Should().Be(incoming);
    }

    [Fact]
    public async Task Missing_id_is_generated_and_echoed()
    {
        await using var app = await StartAsync();

        var response = await app.Client.GetAsync("/c");

        var echoed = response.Headers.GetValues(CorrelationExtensions.HeaderName).Single();
        echoed.Should().NotBeNullOrWhiteSpace();
        (await response.Content.ReadAsStringAsync()).Should().Be(echoed);
    }

    [Theory]
    [InlineData("has space")]
    [InlineData("evil\u0001ctl")]
    public async Task Unsafe_values_are_replaced(string incoming)
    {
        await using var app = await StartAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/c");
        request.Headers.TryAddWithoutValidation(CorrelationExtensions.HeaderName, incoming);

        var response = await app.Client.SendAsync(request);

        response.Headers.GetValues(CorrelationExtensions.HeaderName).Single().Should().NotBe(incoming);
    }

    [Fact]
    public void Oversized_values_are_rejected()
    {
        CorrelationExtensions.Sanitize(new string('a', 129)).Should().BeNull();
        CorrelationExtensions.Sanitize(new string('a', 128)).Should().HaveLength(128);
    }

    [Fact]
    public async Task Correlation_id_is_pushed_into_the_logging_scope()
    {
        await using var app = await StartAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/c");
        request.Headers.Add(CorrelationExtensions.HeaderName, "scope-check-1");

        await app.Client.SendAsync(request);

        app.Logs.Entries.Should().Contain(e =>
            e.Message == "inside endpoint" &&
            e.Scope.ContainsKey("CorrelationId") && (string?)e.Scope["CorrelationId"] == "scope-check-1");
    }
}

public sealed class CorsGuardTests
{
    private static IConfiguration Config(params string[] origins) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(origins.Select((o, i) =>
                new KeyValuePair<string, string?>($"Cors:AllowedOrigins:{i}", o)))
            .Build();

    private static IHostEnvironment Env(string name) => new HostingEnvironment { EnvironmentName = name };

    [Theory]
    [InlineData("Production")]
    [InlineData("Docker")]
    [InlineData("Staging")]
    public void Empty_origins_fail_startup_outside_development(string environment)
    {
        var act = () => new ServiceCollection().AddConfigurableCors(Config(), Env(environment));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Cors:AllowedOrigins*").And.Message.Should().Contain(environment);
    }

    [Fact]
    public void Empty_origins_are_tolerated_in_development()
    {
        var act = () => new ServiceCollection().AddConfigurableCors(Config(), Env("Development"));

        act.Should().NotThrow();
    }

    [Fact]
    public void Blank_entries_count_as_empty()
    {
        var act = () => new ServiceCollection().AddConfigurableCors(Config(" ", ""), Env("Production"));

        act.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData("https://app.example.com")]
    [InlineData("*")]
    public void Configured_origins_are_accepted_in_production(string origin)
    {
        var act = () => new ServiceCollection().AddConfigurableCors(Config(origin), Env("Production"));

        act.Should().NotThrow();
    }
}
