using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Api.E2ETests.Infrastructure;
using FluentAssertions;
using Identity.Domains.Users.Models;
using Microsoft.AspNetCore.Identity;
using Hangfire;
using Hangfire.Common;
using Microsoft.Extensions.DependencyInjection;
using Shared.Core.Authorization;
using Xunit;

namespace Api.E2ETests.Identity;

/// <summary>
/// Email confirmation over real HTTP:
/// <list type="bullet">
/// <item>Registering by email queues a confirmation mail.</item>
/// <item>Confirm accepts only a valid token, and a token issued before a password change is dead.</item>
/// <item>Once confirmed, forgot-password by email works.</item>
/// <item>Send-confirmation is caller-only.</item>
/// </list>
/// </summary>
[Collection(E2ECollection.Name)]
public class EmailConfirmationFlowTests(E2ETestFixture fixture)
{
    private const string Password = "E2e!Passw0rd";
    private const string Confirm = "/api/auth/email/confirm";
    private const string SendConfirmation = "/api/auth/email/send-confirmation";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed record Envelope<T>(bool IsSuccess, T? Data, string? Message);

    private sealed record RegisterData(string AccessToken);

    private sealed record MessageData(string Message);

    private async Task<(Guid UserId, string AccessToken)> RegisterAsync(string email)
    {
        var response = await fixture.CreateAnonymousClient().PostAsJsonAsync("/api/auth/register",
            new { identifier = email, password = Password, confirmPassword = Password, firstName = "Ada", lastName = "L" });
        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, text);
        var token = JsonSerializer.Deserialize<Envelope<RegisterData>>(text, Json)!.Data!.AccessToken;

        var userId = await fixture.WithScopeAsync(async sp =>
            (await sp.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync(email))!.Id);
        return (userId, token);
    }

    private Task<ApplicationUser> ReloadAsync(Guid userId) => fixture.WithScopeAsync(async sp =>
        (await sp.GetRequiredService<UserManager<ApplicationUser>>().FindByIdAsync(userId.ToString()))!);

    /// <summary>What the email link carries (the mail itself goes through the async notification pipeline).</summary>
    private Task<string> IssueTokenAsync(Guid userId) => fixture.WithScopeAsync(async sp =>
    {
        var users = sp.GetRequiredService<UserManager<ApplicationUser>>();
        return await users.GenerateEmailConfirmationTokenAsync((await users.FindByIdAsync(userId.ToString()))!);
    });

    private static int DispatchJobsAddressedTo(JobStorage storage, string email)
    {
        var api = storage.GetMonitoringApi();
        var jobs = new List<Job?>();
        foreach (var queue in api.Queues())
        {
            jobs.AddRange(api.EnqueuedJobs(queue.Name, 0, 1000).Select(j => j.Value.Job));
        }

        jobs.AddRange(api.ProcessingJobs(0, 1000).Select(j => j.Value.Job));
        jobs.AddRange(api.SucceededJobs(0, 1000).Select(j => j.Value.Job));
        jobs.AddRange(api.FailedJobs(0, 1000).Select(j => j.Value.Job));
        jobs.AddRange(api.ScheduledJobs(0, 1000).Select(j => j.Value.Job));

        return jobs.Count(job => job is not null &&
                                 job.Type.Name == "NotificationDispatchJob" &&
                                 job.Args.Count > 0 && JsonSerializer.Serialize(job.Args[0]).Contains(email, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Register_queues_a_confirmation_and_only_a_valid_token_confirms()
    {
        var email = $"confirm-{Guid.NewGuid():N}@e2e.test";
        var (userId, _) = await RegisterAsync(email);

        (await ReloadAsync(userId)).EmailConfirmed.Should().BeFalse();

        // The confirmation mail was handed to the Notification module: a dispatch job addressed to this email exists
        // in the app's Hangfire storage as soon as register returns (the delivery-log row appears only when a worker
        // runs the job, which is slow under suite load). Any state counts: a worker may already have picked it up.
        var queued = await fixture.WithScopeAsync(sp =>
            Task.FromResult(DispatchJobsAddressedTo(sp.GetRequiredService<JobStorage>(), email)));

        queued.Should().Be(1, "registering by email sends exactly one confirmation link");

        var anonymous = fixture.CreateAnonymousClient();

        // Wrong token and unknown user look identical.
        (await anonymous.PostAsJsonAsync(Confirm, new { userId, token = "not-a-token" }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await anonymous.PostAsJsonAsync(Confirm, new { userId = Guid.NewGuid(), token = await IssueTokenAsync(userId) }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var ok = await anonymous.PostAsJsonAsync(Confirm, new { userId, token = await IssueTokenAsync(userId) });
        ok.StatusCode.Should().Be(HttpStatusCode.OK, await ok.Content.ReadAsStringAsync());
        (await ReloadAsync(userId)).EmailConfirmed.Should().BeTrue();

        // Confirmed email is now eligible for forgot-password (generic 200 either way, but no longer a dead end).
        (await anonymous.PostAsJsonAsync("/api/auth/forgot-password", new { identifier = email }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_link_issued_before_a_password_change_no_longer_confirms()
    {
        var (userId, _) = await RegisterAsync($"stale-{Guid.NewGuid():N}@e2e.test");
        var staleToken = await IssueTokenAsync(userId);

        await fixture.WithScopeAsync(async sp =>
        {
            var users = sp.GetRequiredService<UserManager<ApplicationUser>>();
            (await users.UpdateSecurityStampAsync((await users.FindByIdAsync(userId.ToString()))!))
                .Succeeded.Should().BeTrue();
        });

        (await fixture.CreateAnonymousClient().PostAsJsonAsync(Confirm, new { userId, token = staleToken }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReloadAsync(userId)).EmailConfirmed.Should().BeFalse();
    }

    [Fact]
    public async Task Send_confirmation_is_for_the_signed_in_caller_only()
    {
        (await fixture.CreateAnonymousClient().PostAsync(SendConfirmation, null))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var (userId, _) = await RegisterAsync($"resend-{Guid.NewGuid():N}@e2e.test");
        var caller = fixture.CreateClientAs(userId, RoleConstants.User);

        var sent = await caller.PostAsync(SendConfirmation, null);
        sent.StatusCode.Should().Be(HttpStatusCode.OK, await sent.Content.ReadAsStringAsync());

        var ok = await fixture.CreateAnonymousClient()
            .PostAsJsonAsync(Confirm, new { userId, token = await IssueTokenAsync(userId) });
        ok.StatusCode.Should().Be(HttpStatusCode.OK);

        var again = await caller.PostAsync(SendConfirmation, null);
        var body = JsonSerializer.Deserialize<Envelope<MessageData>>(await again.Content.ReadAsStringAsync(), Json)!;
        again.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Data!.Message.Should().Contain("already confirmed");
    }
}
