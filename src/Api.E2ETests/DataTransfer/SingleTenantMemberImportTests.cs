using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Api.E2ETests.Infrastructure;
using FluentAssertions;
using Identity.Domains.Users.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Shared.Core.Authorization;
using Xunit;

namespace Api.E2ETests.DataTransfer;

/// <summary>
/// <c>tenancy.members</c> import in SINGLE-tenant mode through the real job path (Hangfire + TransferJobRunner +
/// ActingUser): the requester has no membership, so the platform roles resolved from the user directory
/// (<c>GetUserRolesQuery</c>, not the token: there is no HTTP request in the job) decide what they may grant.
/// </summary>
[Collection(E2ECollection.Name)]
public class SingleTenantMemberImportTests(E2ETestFixture fixture)
{
    private const string Base = "/api/v1/data-transfer";
    private static readonly TimeSpan JobTimeout = TimeSpan.FromSeconds(60);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private enum JobStatus { Pending = 1, Running = 2, Completed = 3, CompletedWithErrors = 4, Failed = 5, Cancelled = 6 }

    private sealed record Envelope<T>(bool IsSuccess, T? Data);

    private sealed record Job(Guid Id, JobStatus Status, long CreatedRows, long FailedRows);

    private sealed record JobDetail(Job Job, List<RowError> FirstErrors);

    private sealed record RowError(long RowNumber, string? Column, string Code);

    [Fact]
    public async Task A_platform_admin_without_a_membership_invites_with_a_role_and_a_dry_run_invites_nobody()
    {
        var admin = await SeedUserAsync(RoleConstants.User, RoleConstants.Admin);
        var client = fixture.CreateClientAs(admin, RoleConstants.User, RoleConstants.Admin);
        var email = Email();
        var csv = $"email,roles\n{email},Member\n";

        var dry = await RunAsync(client, csv, "DryRun");
        dry.Job.Should().BeEquivalentTo(new { Status = JobStatus.Completed, CreatedRows = 1L, FailedRows = 0L },
            o => o.ExcludingMissingMembers());
        (await client.GetStringAsync("/api/v1/tenant-invitations")).Should().NotContain(email, "a dry run invites nobody");

        var commit = await client.PostAsJsonAsync($"{Base}/imports/{dry.Job.Id}/commit", new { });
        commit.StatusCode.Should().Be(HttpStatusCode.Accepted, await commit.Content.ReadAsStringAsync());
        var commitJob = (await commit.Content.ReadFromJsonAsync<Envelope<Job>>(Json))!.Data!;
        (await WaitAsync(client, commitJob.Id)).Job.Should().BeEquivalentTo(
            new { Status = JobStatus.Completed, CreatedRows = 1L }, o => o.ExcludingMissingMembers());
        (await client.GetStringAsync("/api/v1/tenant-invitations")).Should().Contain(email);
    }

    [Fact]
    public async Task A_requester_whose_directory_roles_are_only_User_gets_a_forbidden_row_error()
    {
        // The token claims Admin (so the route lets the job start) but the DIRECTORY, which the job trusts, says User.
        var demoted = await SeedUserAsync(RoleConstants.User);
        var client = fixture.CreateClientAs(demoted, RoleConstants.User, RoleConstants.Admin);
        var email = Email();

        var done = await RunAsync(client, $"email,roles\n{email},Member\n", "Commit");

        done.Job.CreatedRows.Should().Be(0);
        done.FirstErrors.Should().ContainSingle(e => e.RowNumber == 2 && e.Code == "forbidden");
        (await client.GetStringAsync("/api/v1/tenant-invitations")).Should().NotContain(email);
    }

    private static string Email() => $"st-{Guid.NewGuid():N}@example.com";

    private Task<Guid> SeedUserAsync(params string[] roles) =>
        fixture.WithScopeAsync(async sp =>
        {
            var users = sp.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(), UserName = $"st{Guid.NewGuid():N}", Email = Email(), EmailConfirmed = true,
                IsActive = true, IsProfileComplete = true
            };
            (await users.CreateAsync(user, "P@ssw0rd!Strong1")).Succeeded.Should().BeTrue();
            (await users.AddToRolesAsync(user, roles)).Succeeded.Should().BeTrue();
            return user.Id;
        });

    private static async Task<JobDetail> RunAsync(HttpClient client, string csv, string mode)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        form.Add(file, "file", "members.csv");
        form.Add(new StringContent("tenancy.members"), "definitionKey");
        form.Add(new StringContent(mode), "mode");
        using var response = await client.PostAsync($"{Base}/imports", form);
        response.StatusCode.Should().Be(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync());
        var job = (await response.Content.ReadFromJsonAsync<Envelope<Job>>(Json))!.Data!;
        return await WaitAsync(client, job.Id);
    }

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
