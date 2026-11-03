using System.Text.Json;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Shared.Core.Exceptions;
using Shared.Core.Exceptions.Handler;
using Shared.Core.ProblemDetails;
using Shared.Core.Tests.Infrastructure;

namespace Shared.Core.Tests.Exceptions;

public sealed class ExceptionHandlingTests
{
    private const string DbSecret = "duplicate key value violates unique constraint \"ix_identity_users_email\" on table \"asp_net_users\"";

    public static TheoryData<string, int> Cases => new()
    {
        { "validation", 400 },
        { "badrequest", 400 },
        { "domain", 400 },
        { "app", 400 },
        { "unauthorized", 401 },
        { "forbidden", 403 },
        { "notfound", 404 },
        { "notfound-key", 404 },
        { "aggregate-notfound", 404 },
        { "conflict", 409 },
        { "concurrency", 409 },
        { "ef-concurrency", 409 },
        { "gone", 410 },
        { "custom-422", 422 },
        { "internal", 500 },
        { "db", 500 },
        { "plain", 500 },
        { "bad-http-request", 400 }
    };

    private static Exception Create(string kind) => kind switch
    {
        "validation" => new ValidationException([new ValidationFailure("Name", "Name is required."), new ValidationFailure("Price", "Price must be positive.")]),
        "badrequest" => new BadRequestException("Bad input."),
        "domain" => new DomainException("Rule violated."),
        "app" => new AppException("App rule."),
        "unauthorized" => new UnauthorizedException("Not logged in."),
        "forbidden" => new ForbiddenException("Not yours."),
        "notfound" => new NotFoundException("Product not found."),
        "notfound-key" => new NotFoundException("Product", Guid.Empty),
        "aggregate-notfound" => AggregateNotFoundException.For<ExceptionHandlingTests>(Guid.Empty),
        "conflict" => new ConflictException("Already exists."),
        "concurrency" => new ConcurrencyConflictException(),
        "ef-concurrency" => new DbUpdateConcurrencyException("db says: " + DbSecret),
        "gone" => new GoneException("Expired."),
        "custom-422" => new CustomException("Unprocessable.", System.Net.HttpStatusCode.UnprocessableEntity, code: 4221),
        "internal" => new InternalServerException("Payment gateway key {0} invalid", null, "sk_live_secret"),
        "db" => new DbUpdateException("An error occurred while saving the entity changes.", new Exception(DbSecret)),
        "plain" => new InvalidOperationException("Sequence contains no elements; connection string Host=db;Password=pw"),
        "bad-http-request" => new BadHttpRequestException("Failed to read parameter \"request\" from the request body as JSON.", 400),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Mapper_maps_every_exception_type(string kind, int expectedStatus)
    {
        ExceptionStatusMapper.Map(Create(kind)).StatusCode.Should().Be(expectedStatus);
    }

    private static Task<TestApp> StartAsync() =>
        TestApp.StartAsync(
            b => b.Services.AddCustomProblemDetails(),
            app =>
            {
                app.UseCustomProblemDetails();
                app.MapGet("/throw/{kind}", (string kind) => { throw Create(kind); });
                app.MapGet("/status/{code:int}", (int code) => Results.StatusCode(code));
            });

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Pipeline_returns_the_mapped_status_in_the_ApiResponse_envelope(string kind, int expectedStatus)
    {
        await using var app = await StartAsync();

        var response = await app.Client.GetAsync($"/throw/{kind}");
        var body = await response.Content.ReadAsStringAsync();

        ((int)response.StatusCode).Should().Be(expectedStatus);
        using var json = JsonDocument.Parse(body);
        json.RootElement.GetProperty("isSuccess").GetBoolean().Should().BeFalse();
        json.RootElement.GetProperty("statusCode").GetInt32().Should().Be(expectedStatus);
        json.RootElement.GetProperty("errors").GetArrayLength().Should().BeGreaterThan(0);
        json.RootElement.GetProperty("traceId").GetString().Should().NotBeNullOrEmpty();
        json.RootElement.TryGetProperty("data", out _).Should().BeTrue();
    }

    [Theory]
    [InlineData("db")]
    [InlineData("plain")]
    [InlineData("internal")]
    public async Task Server_errors_never_leak_exception_or_database_details(string kind)
    {
        await using var app = await StartAsync();

        var response = await app.Client.GetAsync($"/throw/{kind}");
        var body = await response.Content.ReadAsStringAsync();

        ((int)response.StatusCode).Should().Be(500);
        body.Should().Contain(ExceptionStatusMapper.GenericServerError);
        body.Should().NotContainAny("ix_identity_users_email", "asp_net_users", "duplicate key", "Password=pw",
            "Sequence contains", "sk_live_secret", "saving the entity");
    }

    [Fact]
    public async Task Concurrency_conflict_from_ef_does_not_leak_the_db_message()
    {
        await using var app = await StartAsync();

        var body = await (await app.Client.GetAsync("/throw/ef-concurrency")).Content.ReadAsStringAsync();

        body.Should().NotContain("ix_identity_users_email").And.Contain("modified by another request");
    }

    [Fact]
    public async Task Server_errors_are_logged_with_the_exception_object()
    {
        await using var app = await StartAsync();

        await app.Client.GetAsync("/throw/db");

        var entry = app.Logs.Entries.Should().ContainSingle(e => e.Level == LogLevel.Error && e.Exception != null).Subject;
        entry.Exception.Should().BeOfType<DbUpdateException>();
        entry.Exception!.InnerException!.Message.Should().Contain("ix_identity_users_email", "the full details stay in the server log");
    }

    [Fact]
    public async Task Client_errors_are_not_logged_as_errors()
    {
        await using var app = await StartAsync();

        await app.Client.GetAsync("/throw/notfound");

        app.Logs.Entries.Should().NotContain(e => e.Level >= LogLevel.Error);
    }

    [Fact]
    public async Task Validation_errors_list_every_message()
    {
        await using var app = await StartAsync();

        var body = await (await app.Client.GetAsync("/throw/validation")).Content.ReadAsStringAsync();

        using var json = JsonDocument.Parse(body);
        json.RootElement.GetProperty("errors").EnumerateArray().Select(e => e.GetString())
            .Should().BeEquivalentTo("Name is required.", "Price must be positive.");
    }

    [Fact]
    public async Task Custom_error_code_is_returned()
    {
        await using var app = await StartAsync();

        var body = await (await app.Client.GetAsync("/throw/custom-422")).Content.ReadAsStringAsync();

        JsonDocument.Parse(body).RootElement.GetProperty("code").GetInt32().Should().Be(4221);
    }

    [Fact]
    public async Task Bodiless_error_statuses_get_the_envelope_from_status_code_pages()
    {
        await using var app = await StartAsync();

        var response = await app.Client.GetAsync("/status/403");
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        ((int)response.StatusCode).Should().Be(403);
        json.GetProperty("isSuccess").GetBoolean().Should().BeFalse();
        json.GetProperty("errors")[0].GetString().Should().Be("Forbidden");
    }

    [Fact]
    public void NotFoundException_name_key_constructor_is_a_404()
    {
        new NotFoundException("Tag", Guid.Empty).StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);
    }

    [Fact]
    public void InternalServerException_formats_its_message_and_keeps_the_status()
    {
        var ex = new InternalServerException("Failed {0} of {1}", 7, "a", "b");

        ex.Message.Should().Be("Failed a of b");
        ex.Code.Should().Be(7);
        ex.StatusCode.Should().Be(System.Net.HttpStatusCode.InternalServerError);
    }
}
