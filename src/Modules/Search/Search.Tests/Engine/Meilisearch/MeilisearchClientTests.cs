using System.Net;
using System.Text;
using Search.Engine;
using Search.Engine.Meilisearch;
using Search.Options;

namespace Search.Tests.Engine.Meilisearch;

/// <summary>The transport's contract: what is "unavailable" (fall back, retry) and what is a refusal (a bug).</summary>
public sealed class MeilisearchClientTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Paths.Add(request.RequestUri!.PathAndQuery);
            return Task.FromResult(respond(request));
        }
    }

    private static MeilisearchClient Client(HttpMessageHandler handler, int taskTimeoutSeconds = 30) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://meilisearch:7700/") },
            Microsoft.Extensions.Options.Options.Create(new SearchOptions
            {
                Meilisearch = new SearchMeilisearchOptions { TaskTimeoutSeconds = taskTimeoutSeconds }
            }),
            TimeProvider.System);

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    public async Task Server_side_failures_mean_the_engine_is_unavailable(HttpStatusCode status)
    {
        var client = Client(new StubHandler(_ => Json(status, "{}")));

        await FluentActions.Awaiting(() => client.SendAsync(HttpMethod.Get, "indexes", null, CancellationToken.None))
            .Should().ThrowAsync<SearchEngineUnavailableException>();
    }

    [Fact]
    public async Task A_network_failure_means_the_engine_is_unavailable()
    {
        var client = Client(new StubHandler(_ => throw new HttpRequestException("connection refused")));

        await FluentActions.Awaiting(() => client.SendAsync(HttpMethod.Get, "indexes", null, CancellationToken.None))
            .Should().ThrowAsync<SearchEngineUnavailableException>();
    }

    [Fact]
    public async Task A_refused_request_is_an_api_error_with_meilisearchs_code()
    {
        var client = Client(new StubHandler(_ => Json(HttpStatusCode.BadRequest,
            """{"message":"Attribute `f_x` is not filterable.","code":"invalid_search_filter"}""")));

        var error = await FluentActions.Awaiting(() => client.SendAsync(HttpMethod.Post, "indexes/i/search", null, CancellationToken.None))
            .Should().ThrowAsync<MeilisearchApiException>();
        error.Which.Code.Should().Be("invalid_search_filter");
        error.Which.Status.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task The_callers_own_cancellation_is_not_an_outage()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var client = Client(new StubHandler(_ => Json(HttpStatusCode.OK, "{}")));

        await FluentActions.Awaiting(() => client.SendAsync(HttpMethod.Get, "indexes", null, cancelled.Token))
            .Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task A_write_returns_once_its_task_has_succeeded()
    {
        var polls = 0;
        var handler = new StubHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/indexes/i/documents" => Json(HttpStatusCode.Accepted, """{"taskUid":42}"""),
            "/tasks/42" => Json(HttpStatusCode.OK, ++polls < 3
                ? """{"uid":42,"status":"processing"}"""
                : """{"uid":42,"status":"succeeded","details":{"indexedDocuments":1}}"""),
            _ => Json(HttpStatusCode.NotFound, "{}")
        });

        var task = await Client(handler).RunTaskAsync(HttpMethod.Post, "indexes/i/documents", new System.Text.Json.Nodes.JsonArray(), CancellationToken.None);

        task["details"]!["indexedDocuments"]!.GetValue<int>().Should().Be(1);
        handler.Paths.Should().Equal("/indexes/i/documents", "/tasks/42", "/tasks/42", "/tasks/42");
    }

    [Fact]
    public async Task A_failed_task_is_an_api_error()
    {
        var handler = new StubHandler(request => request.RequestUri!.AbsolutePath == "/tasks/7"
            ? Json(HttpStatusCode.OK, """{"uid":7,"status":"failed","error":{"code":"index_not_found","message":"Index `i` not found."}}""")
            : Json(HttpStatusCode.Accepted, """{"taskUid":7}"""));

        var error = await FluentActions.Awaiting(() => Client(handler).RunTaskAsync(HttpMethod.Post, "indexes/i/documents/delete-batch", null, CancellationToken.None))
            .Should().ThrowAsync<MeilisearchApiException>();
        error.Which.Code.Should().Be(MeilisearchApiException.IndexNotFound);
    }

    [Fact]
    public async Task A_task_that_does_not_finish_in_time_means_the_engine_is_unavailable()
    {
        var handler = new StubHandler(request => request.RequestUri!.AbsolutePath == "/tasks/9"
            ? Json(HttpStatusCode.OK, """{"uid":9,"status":"enqueued"}""")
            : Json(HttpStatusCode.Accepted, """{"taskUid":9}"""));

        await FluentActions.Awaiting(() => Client(handler, taskTimeoutSeconds: 1).RunTaskAsync(HttpMethod.Post, "indexes", null, CancellationToken.None))
            .Should().ThrowAsync<SearchEngineUnavailableException>().WithMessage("*did not finish*");
    }

    [Fact]
    public async Task Health_is_available_or_not_never_an_exception()
    {
        (await Client(new StubHandler(_ => Json(HttpStatusCode.OK, """{"status":"available"}"""))).IsHealthyAsync(CancellationToken.None))
            .Should().BeTrue();
        (await Client(new StubHandler(_ => throw new HttpRequestException("down"))).IsHealthyAsync(CancellationToken.None))
            .Should().BeFalse();
    }
}
