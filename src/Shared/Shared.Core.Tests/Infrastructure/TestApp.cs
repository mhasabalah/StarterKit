using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Shared.Core.Tests.Infrastructure;

/// <summary>Minimal in-process host on <see cref="TestServer"/> for pipeline tests.</summary>
public sealed class TestApp : IAsyncDisposable
{
    public const string TestUserHeader = "X-Test-User";

    private TestApp(WebApplication app, CapturingLoggerProvider logs)
    {
        App = app;
        Logs = logs;
    }

    public WebApplication App { get; }
    public CapturingLoggerProvider Logs { get; }
    public TestServer Server => App.GetTestServer();
    public HttpClient Client => App.GetTestClient();

    public static async Task<TestApp> StartAsync(
        Action<WebApplicationBuilder>? configureServices,
        Action<WebApplication> configure,
        IDictionary<string, string?>? configuration = null,
        string environment = "Production")
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        builder.WebHost.UseTestServer();
        builder.Configuration.Sources.Clear();
        if (configuration is not null) builder.Configuration.AddInMemoryCollection(configuration);

        var logs = new CapturingLoggerProvider();
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(logs);
        builder.Logging.SetMinimumLevel(LogLevel.Trace);

        configureServices?.Invoke(builder);

        var app = builder.Build();
        configure(app);
        await app.StartAsync();
        return new TestApp(app, logs);
    }

    /// <summary>Test-only authentication: header <see cref="TestUserHeader"/> = user id.</summary>
    public static void UseHeaderUser(IApplicationBuilder app) => app.Use((ctx, next) =>
    {
        if (ctx.Request.Headers.TryGetValue(TestUserHeader, out var id) && !string.IsNullOrEmpty(id))
            ctx.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id!)], "Test"));
        return next(ctx);
    });

    /// <summary>Sends a request with an explicit TCP peer address (what Kestrel would put in RemoteIpAddress).</summary>
    public Task<HttpContext> SendAsync(string path, string remoteIp, Action<HttpRequest>? configure = null,
        string method = "GET") =>
        Server.SendAsync(ctx =>
        {
            ctx.Request.Method = method;
            ctx.Request.Path = path;
            ctx.Connection.RemoteIpAddress = IPAddress.Parse(remoteIp);
            configure?.Invoke(ctx.Request);
        });

    public async ValueTask DisposeAsync()
    {
        await App.StopAsync();
        await App.DisposeAsync();
    }
}

public static class HttpContextTestExtensions
{
    public static async Task<string> ReadBodyAsync(this HttpContext context)
    {
        if (context.Response.Body.CanSeek) context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body);
        return await reader.ReadToEndAsync();
    }
}
