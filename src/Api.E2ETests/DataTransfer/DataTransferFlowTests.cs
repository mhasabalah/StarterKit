using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Api.E2ETests.Infrastructure;
using FluentAssertions;
using Shared.Core.Authorization;
using Xunit;

namespace Api.E2ETests.DataTransfer;

/// <summary>
/// Bulk import/export over real HTTP against the booted API: multipart upload through Attachment, 202 + job,
/// the REAL Hangfire <c>data-transfer</c> server running the job, the Catalog definition writing products, the dry
/// run → commit flow, error report and export downloads, and the authorization of every route.
/// </summary>
[Collection(E2ECollection.Name)]
public class DataTransferFlowTests(E2ETestFixture fixture)
{
    private const string Base = "/api/v1/data-transfer";
    private static readonly TimeSpan JobTimeout = TimeSpan.FromSeconds(60);

    /// <summary>The API serializes enums as numbers (names come from the enum lookup endpoint).</summary>
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private enum JobStatus { Pending = 1, Running = 2, Completed = 3, CompletedWithErrors = 4, Failed = 5, Cancelled = 6 }

    private sealed record Envelope<T>(bool IsSuccess, T? Data, List<string>? Errors, string? ErrorCode);

    private sealed record Job(Guid Id, JobStatus Status, long ProcessedRows, long CreatedRows, long FailedRows,
        bool HasResult, bool HasErrorReport, string? FailureCode, long Version);

    private sealed record JobDetail(Job Job, List<RowError> FirstErrors);

    private sealed record RowError(long RowNumber, string? Column, string Code, string Message);

    private sealed record FileLink(string Url, string FileName);

    [Fact]
    public async Task Dry_run_then_commit_imports_products_and_reports_the_invalid_row()
    {
        var admin = fixture.CreateClientAs(Guid.NewGuid(), RoleConstants.Admin);
        var good = $"E2E-{Guid.NewGuid():N}"[..20].ToUpperInvariant();
        var csv = "sku,name_en,name_ar,type,price,currency\n" +
                  $"{good},Imported,مستورد,Physical,12.50,EGP\n" +
                  "BAD-ROW,Broken,مكسور,Physical,not-a-price,EGP\n";

        var dryRun = await StartImportAsync(admin, csv, "DryRun");
        var dryDone = await WaitAsync(admin, dryRun.Id);
        dryDone.Job.Should().BeEquivalentTo(new { Status = JobStatus.CompletedWithErrors, CreatedRows = 1L, FailedRows = 1L },
            o => o.ExcludingMissingMembers());
        dryDone.FirstErrors.Should().ContainSingle(e => e.RowNumber == 3 && e.Column == "price" && e.Code == "invalid_number");

        var report = await admin.GetAsync($"{Base}/jobs/{dryRun.Id}/files/ErrorReport");
        report.StatusCode.Should().Be(HttpStatusCode.OK, await report.Content.ReadAsStringAsync());

        var commit = await admin.PostAsJsonAsync($"{Base}/imports/{dryRun.Id}/commit", new { });
        commit.StatusCode.Should().Be(HttpStatusCode.Accepted, await commit.Content.ReadAsStringAsync());
        var commitJob = (await commit.Content.ReadFromJsonAsync<Envelope<Job>>(Json))!.Data!;

        (await WaitAsync(admin, commitJob.Id)).Job.Status.Should().Be(JobStatus.CompletedWithErrors);
        var products = await admin.GetStringAsync($"/api/v1/products/my?search={good}");
        products.Should().Contain(good);
    }

    [Fact]
    public async Task Export_produces_a_downloadable_workbook()
    {
        var admin = fixture.CreateClientAs(Guid.NewGuid(), RoleConstants.Admin);

        var start = await admin.PostAsJsonAsync($"{Base}/exports", new { definitionKey = "catalog.products", format = 2 /* Xlsx: enums are numbers in JSON */ });
        start.StatusCode.Should().Be(HttpStatusCode.Accepted, await start.Content.ReadAsStringAsync());
        start.Headers.Location!.ToString().Should().Contain("/data-transfer/jobs/");
        var job = (await start.Content.ReadFromJsonAsync<Envelope<Job>>(Json))!.Data!;

        var done = await WaitAsync(admin, job.Id);
        done.Job.Status.Should().Be(JobStatus.Completed);
        done.Job.HasResult.Should().BeTrue();

        var link = await admin.GetFromJsonAsync<Envelope<FileLink>>($"{Base}/jobs/{job.Id}/files/Result", Json);
        link!.Data!.FileName.Should().EndWith(".xlsx");
    }

    [Fact]
    public async Task Template_is_an_xlsx_with_the_product_headers()
    {
        var admin = fixture.CreateClientAs(Guid.NewGuid(), RoleConstants.Admin);

        var template = await admin.GetAsync($"{Base}/definitions/catalog.products/template?format=Xlsx");

        template.StatusCode.Should().Be(HttpStatusCode.OK);
        template.Content.Headers.ContentType!.MediaType.Should().Be("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        (await template.Content.ReadAsByteArrayAsync()).Take(2).Should().Equal((byte)'P', (byte)'K');
        (await admin.GetStringAsync($"{Base}/definitions")).Should().Contain("catalog.products");
    }

    [Fact]
    public async Task Without_the_permissions_every_route_is_refused()
    {
        var user = fixture.CreateClientAs(Guid.NewGuid(), RoleConstants.User);
        var anonymous = fixture.CreateAnonymousClient();

        (await StartImportRawAsync(user, "sku\nA\n", "DryRun")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await user.PostAsJsonAsync($"{Base}/exports", new { definitionKey = "catalog.products" })).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
        (await user.GetAsync($"{Base}/definitions/catalog.products/template")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await anonymous.GetAsync($"{Base}/jobs")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync($"{Base}/definitions")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Someone_elses_job_is_not_found_and_an_unknown_definition_is_404()
    {
        var admin = fixture.CreateClientAs(Guid.NewGuid(), RoleConstants.Admin);
        var job = await StartImportAsync(admin, "sku,name_en,name_ar,type,price,currency\n", "DryRun");
        await WaitAsync(admin, job.Id);
        var otherUser = fixture.CreateClientAs(Guid.NewGuid(), RoleConstants.User);

        (await otherUser.PostAsJsonAsync($"{Base}/jobs/{job.Id}/cancel", new { })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await admin.PostAsJsonAsync($"{Base}/exports", new { definitionKey = "nope.things" })).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_file_that_is_not_csv_or_xlsx_is_rejected_with_400()
    {
        var admin = fixture.CreateClientAs(Guid.NewGuid(), RoleConstants.Admin);
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes("hello"));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        form.Add(file, "file", "notes.pdf");
        form.Add(new StringContent("catalog.products"), "definitionKey");

        var response = await admin.PostAsync($"{Base}/imports", form);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
    }

    private async Task<Job> StartImportAsync(HttpClient client, string csv, string mode)
    {
        var response = await StartImportRawAsync(client, csv, mode);
        response.StatusCode.Should().Be(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<Envelope<Job>>(Json))!.Data!;
    }

    private static Task<HttpResponseMessage> StartImportRawAsync(HttpClient client, string csv, string mode)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        form.Add(file, "file", "products.csv");
        form.Add(new StringContent("catalog.products"), "definitionKey");
        form.Add(new StringContent(mode), "mode");
        return client.PostAsync($"{Base}/imports", form);
    }

    /// <summary>Polls the job (the real Hangfire data-transfer server runs it) until it reaches a terminal status.</summary>
    private static async Task<JobDetail> WaitAsync(HttpClient client, Guid jobId)
    {
        var deadline = DateTime.UtcNow + JobTimeout;
        while (true)
        {
            var detail = (await client.GetFromJsonAsync<Envelope<JobDetail>>($"{Base}/jobs/{jobId}", Json))!.Data!;
            if (detail.Job.Status is JobStatus.Completed or JobStatus.CompletedWithErrors or JobStatus.Failed or JobStatus.Cancelled)
                return detail;
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException($"Job {jobId} still {detail.Job.Status} after {JobTimeout}.");
            await Task.Delay(250);
        }
    }
}
