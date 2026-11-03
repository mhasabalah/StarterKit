using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Shared.Core.Response;
using Shared.Core.Tests.Infrastructure;
using Shared.Core.Web;

namespace Shared.Core.Tests.Web;

public sealed class ResponseWrapperTests
{
    private sealed record Dto(int Id, bool IsSuccess, string Note);

    private static Task<TestApp> StartAsync() =>
        TestApp.StartAsync(null, app =>
        {
            app.UseResponseWrapper();
            var api = app.WithApiResponseMarker();
            api.MapGet("/api/no-content", () => Results.NoContent());
            api.MapGet("/api/not-modified", () => Results.StatusCode(304));
            api.MapGet("/api/dto", () => Results.Ok(new { id = 1, name = "x" }));
            api.MapGet("/api/dto-with-issuccess", () => Results.Ok(new Dto(1, true, "has an isSuccess field")));
            api.MapGet("/api/text", () => Results.Text("plain,csv,data", "text/csv"));
            api.MapGet("/api/file", () => Results.Bytes([1, 2, 3, 4], "application/octet-stream"));
            api.MapGet("/api/enveloped", () => Results.Ok(ApiResponseExtensions.Success(new { id = 2 }, "done")));
            api.MapGet("/api/enveloped-json", () => ApiResponseExtensions.SuccessResult(new { id = 3 }));
            api.MapGet("/api/empty-ok", () => Results.Ok());
            app.MapGet("/api/manual-flag", (HttpContext ctx) =>
            {
                ctx.Items[ResponseWrapperMiddleware.AlreadyWrappedItemKey] = true;
                return Results.Json(new { custom = true });
            });
            api.MapGet("/health-like", () => Results.Ok(new { status = "up" }));
        });

    private static async Task<(int Status, string Body, string? ContentType)> GetAsync(TestApp app, string path)
    {
        var response = await app.Client.GetAsync(path);
        return ((int)response.StatusCode, await response.Content.ReadAsStringAsync(),
            response.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData("/api/no-content", 204)]
    [InlineData("/api/not-modified", 304)]
    public async Task Bodiless_statuses_are_never_given_a_body(string path, int status)
    {
        await using var app = await StartAsync();

        var (actualStatus, body, _) = await GetAsync(app, path);

        actualStatus.Should().Be(status);
        body.Should().BeEmpty();
    }

    [Fact]
    public async Task Json_results_are_wrapped()
    {
        await using var app = await StartAsync();

        var (_, body, _) = await GetAsync(app, "/api/dto");

        var json = JsonDocument.Parse(body).RootElement;
        json.GetProperty("isSuccess").GetBoolean().Should().BeTrue();
        json.GetProperty("statusCode").GetInt32().Should().Be(200);
        json.GetProperty("data").GetProperty("name").GetString().Should().Be("x");
    }

    [Fact]
    public async Task A_dto_that_merely_contains_an_isSuccess_field_is_still_wrapped()
    {
        await using var app = await StartAsync();

        var (_, body, _) = await GetAsync(app, "/api/dto-with-issuccess");

        var json = JsonDocument.Parse(body).RootElement;
        json.GetProperty("data").GetProperty("note").GetString().Should().Be("has an isSuccess field");
    }

    [Theory]
    [InlineData("/api/text", "text/csv", "plain,csv,data")]
    [InlineData("/api/file", "application/octet-stream", "\u0001\u0002\u0003\u0004")]
    public async Task Non_json_content_is_passed_through_untouched(string path, string contentType, string expected)
    {
        await using var app = await StartAsync();

        var (_, body, mediaType) = await GetAsync(app, path);

        mediaType.Should().Be(contentType);
        body.Should().Be(expected);
    }

    [Theory]
    [InlineData("/api/enveloped")]
    [InlineData("/api/enveloped-json")]
    [InlineData("/api/manual-flag")]
    public async Task Already_wrapped_responses_are_not_double_wrapped(string path)
    {
        await using var app = await StartAsync();

        var (_, body, _) = await GetAsync(app, path);

        var json = JsonDocument.Parse(body).RootElement;
        if (path == "/api/manual-flag")
        {
            json.GetProperty("custom").GetBoolean().Should().BeTrue();
            return;
        }

        json.GetProperty("isSuccess").GetBoolean().Should().BeTrue();
        json.GetProperty("data").TryGetProperty("isSuccess", out _).Should().BeFalse("the envelope must not be nested");
    }

    [Fact]
    public async Task Empty_ok_is_wrapped_with_null_data()
    {
        await using var app = await StartAsync();

        var (_, body, _) = await GetAsync(app, "/api/empty-ok");

        var json = JsonDocument.Parse(body).RootElement;
        json.GetProperty("isSuccess").GetBoolean().Should().BeTrue();
        json.GetProperty("data").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Paths_outside_api_are_not_wrapped()
    {
        await using var app = await StartAsync();

        var (_, body, _) = await GetAsync(app, "/health-like");

        JsonDocument.Parse(body).RootElement.GetProperty("status").GetString().Should().Be("up");
    }
}
