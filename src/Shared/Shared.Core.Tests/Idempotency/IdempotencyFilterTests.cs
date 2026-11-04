using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Core.Exceptions;
using Shared.Core.Exceptions.Handler;
using Shared.Core.Web;
using Shared.Core.Web.Idempotency;

namespace Shared.Core.Tests.Idempotency;

public class IdempotencyFilterTests
{
    private static readonly IServiceProvider OutputServices = new ServiceCollection().AddLogging().BuildServiceProvider();

    private static IdempotencyFilter NewFilter(IIdempotencyStore? store = null) =>
        new(store ?? NewStore(), Microsoft.Extensions.Options.Options.Create(new IdempotencyOptions()), NullLogger<IdempotencyFilter>.Instance);

    private static IIdempotencyStore NewStore() =>
        new DistributedCacheIdempotencyStore(new MemoryDistributedCache(Microsoft.Extensions.Options.Options.Create(new MemoryDistributedCacheOptions())));

    private static DefaultHttpContext NewContext(string? key, string body = "{}", string user = "user-1")
    {
        var users = new Mock<IHttpContextService>();
        users.Setup(x => x.GetUserId()).Returns(user);
        var http = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddLogging().AddSingleton(users.Object).BuildServiceProvider()
        };
        http.Request.Method = "POST";
        http.Request.Path = "/api/v1/subscriptions";
        if (key is not null)
            http.Request.Headers[IdempotencyExtensions.KeyHeader] = key;
        http.Items[IdempotencyBodyFingerprintMiddleware.FingerprintItemKey] = body;
        return http;
    }

    private static async Task<(int Status, string Body, IHeaderDictionary Headers)> RunAsync(
        IdempotencyFilter filter, HttpContext http, Func<object?> endpoint, Action? onExecuted = null)
    {
        var result = await filter.InvokeAsync(new DefaultEndpointFilterInvocationContext(http), _ =>
        {
            onExecuted?.Invoke();
            return ValueTask.FromResult(endpoint());
        });
        var output = new DefaultHttpContext { RequestServices = OutputServices };
        output.Response.Body = new MemoryStream();
        await ((IResult)result!).ExecuteAsync(output);
        output.Response.Body.Position = 0;
        return (output.Response.StatusCode, await new StreamReader(output.Response.Body).ReadToEndAsync(), output.Response.Headers);
    }

    [Fact]
    public async Task NoHeader_RunsEndpointEveryTime()
    {
        var filter = NewFilter();
        var calls = 0;

        for (var i = 0; i < 2; i++)
            await filter.InvokeAsync(new DefaultEndpointFilterInvocationContext(NewContext(null)), _ =>
            {
                calls++;
                return ValueTask.FromResult<object?>("ok");
            });

        calls.Should().Be(2);
    }

    [Fact]
    public async Task SecondRequestWithSameKey_ReplaysStoredResponse_WithoutRunningEndpoint()
    {
        var filter = NewFilter();
        var calls = 0;

        var first = await RunAsync(filter, NewContext("k1"), () => Results.Created("/x/1", new { id = 1 }), () => calls++);
        var second = await RunAsync(filter, NewContext("k1"), () => Results.Created("/x/2", new { id = 2 }), () => calls++);

        calls.Should().Be(1);
        first.Headers.ContainsKey(IdempotencyExtensions.ReplayedHeader).Should().BeFalse();
        second.Status.Should().Be(201);
        second.Body.Should().Be(first.Body).And.Contain("\"id\":1");
        second.Headers[IdempotencyExtensions.ReplayedHeader].ToString().Should().Be("true");
        second.Headers.Location.ToString().Should().Be("/x/1");
    }

    [Fact]
    public async Task SameKeyWithDifferentBody_IsRejectedWith422_AndCode()
    {
        var filter = NewFilter();
        await RunAsync(filter, NewContext("k1", body: "A"), () => Results.Ok());

        var act = () => RunAsync(filter, NewContext("k1", body: "B"), () => Results.Ok());

        var ex = (await act.Should().ThrowAsync<CustomException>()).Which;
        ex.StatusCode.Should().Be(System.Net.HttpStatusCode.UnprocessableEntity);
        ExceptionStatusMapper.Map(ex).ErrorCode.Should().Be(IdempotencyExtensions.KeyReusedCode);
    }

    [Fact]
    public async Task DuplicateWhileFirstIsInFlight_IsRejectedWith409_AndCode()
    {
        var filter = NewFilter();
        var gate = new TaskCompletionSource();
        var first = filter.InvokeAsync(new DefaultEndpointFilterInvocationContext(NewContext("k1")), async _ =>
        {
            await gate.Task;
            return Results.Ok();
        }).AsTask();

        var act = () => RunAsync(filter, NewContext("k1"), () => Results.Ok());

        var ex = (await act.Should().ThrowAsync<ConflictException>()).Which;
        ex.ErrorCode.Should().Be(IdempotencyExtensions.InProgressCode);
        gate.SetResult();
        await first;
    }

    [Fact]
    public async Task ClientError4xx_IsStoredAndReplayed()
    {
        var filter = NewFilter();
        var calls = 0;

        await RunAsync(filter, NewContext("k1"), () => Results.BadRequest(new { error = "x" }), () => calls++);
        var replay = await RunAsync(filter, NewContext("k1"), () => Results.Ok(), () => calls++);

        calls.Should().Be(1);
        replay.Status.Should().Be(400);
    }

    [Fact]
    public async Task ServerError5xx_ReleasesKey_SoRetryRunsAgain()
    {
        var filter = NewFilter();
        var calls = 0;

        var first = await RunAsync(filter, NewContext("k1"), () => Results.StatusCode(503), () => calls++);
        var retry = await RunAsync(filter, NewContext("k1"), () => Results.Ok(), () => calls++);

        first.Status.Should().Be(503);
        retry.Status.Should().Be(200);
        calls.Should().Be(2);
    }

    [Fact]
    public async Task EndpointException_ReleasesKey_SoRetryRunsAgain()
    {
        var filter = NewFilter();

        var failing = () => filter.InvokeAsync(new DefaultEndpointFilterInvocationContext(NewContext("k1")),
            _ => throw new InvalidOperationException("boom")).AsTask();
        await failing.Should().ThrowAsync<InvalidOperationException>();

        var retry = await RunAsync(filter, NewContext("k1"), () => Results.Ok());
        retry.Status.Should().Be(200);
    }

    [Fact]
    public async Task SameKeyForDifferentUsers_DoesNotCollide()
    {
        var filter = NewFilter();
        var calls = 0;

        await RunAsync(filter, NewContext("k1", user: "alice"), () => Results.Ok(), () => calls++);
        var bob = await RunAsync(filter, NewContext("k1", user: "bob"), () => Results.Ok(), () => calls++);

        calls.Should().Be(2);
        bob.Headers.ContainsKey(IdempotencyExtensions.ReplayedHeader).Should().BeFalse();
    }

    [Fact]
    public async Task StoreUnavailable_ProcessesRequestNormally()
    {
        var store = new Mock<IIdempotencyStore>();
        store.Setup(x => x.TryReserveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("redis down"));
        var calls = 0;

        var result = await NewFilter(store.Object).InvokeAsync(
            new DefaultEndpointFilterInvocationContext(NewContext("k1")), _ =>
            {
                calls++;
                return ValueTask.FromResult<object?>("ok");
            });

        result.Should().Be("ok");
        calls.Should().Be(1);
    }

    [Fact]
    public async Task OversizedKey_IsRejectedWith400()
    {
        var act = () => NewFilter().InvokeAsync(
            new DefaultEndpointFilterInvocationContext(NewContext(new string('k', 300))),
            _ => ValueTask.FromResult<object?>("ok")).AsTask();

        await act.Should().ThrowAsync<BadRequestException>();
    }

    [Fact]
    public async Task Middleware_Fingerprint_IgnoresMultipartBoundary_ButNotContent()
    {
        static async Task<string> Fingerprint(string boundary, string fileContent)
        {
            var http = new DefaultHttpContext();
            http.SetEndpoint(new Endpoint(_ => Task.CompletedTask,
                new EndpointMetadataCollection(new IdempotencyMetadata()), "t"));
            http.Request.Headers[IdempotencyExtensions.KeyHeader] = "k";
            http.Request.ContentType = $"multipart/form-data; boundary={boundary}";
            var body = $"--{boundary}\r\nContent-Disposition: form-data; name=\"a\"\r\n\r\n1\r\n" +
                       $"--{boundary}\r\nContent-Disposition: form-data; name=\"file\"; filename=\"p.png\"\r\n\r\n{fileContent}\r\n--{boundary}--\r\n";
            http.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
            await new IdempotencyBodyFingerprintMiddleware(_ => Task.CompletedTask).InvokeAsync(http);
            return (string)http.Items[IdempotencyBodyFingerprintMiddleware.FingerprintItemKey]!;
        }

        var a = await Fingerprint("AAA", "data");
        var b = await Fingerprint("BBB", "data");
        var c = await Fingerprint("AAA", "other");

        b.Should().Be(a);
        c.Should().NotBe(a);
    }
}
