using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Api.E2ETests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Core.Authorization;
using Shared.Core.Messaging.Outbox;
using Shared.Core.MultiTenancy;
using Subscriptions.Data;
using Xunit;

namespace Api.E2ETests.Messaging;

/// <summary>Dead-letter admin endpoints over real HTTP: permission gate, list, requeue, and the failure modes.</summary>
[Collection(E2ECollection.Name)]
public class OutboxAdminTests(E2ETestFixture fixture)
{
    private const string Base = "/api/v1/admin/outbox/SubscriptionsDbContext/dead-letters";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed record Envelope<T>(bool IsSuccess, T? Data);
    private sealed record Page(List<Row>? Result);
    private sealed record Row(Guid Id, string? LastError);
    private sealed record RequeueData(int Requeued);

    private HttpClient As(string role)
    {
        var client = fixture.CreateClientAs(Guid.NewGuid(), role);
        client.DefaultRequestHeaders.Add(TestAuthHandler.MfaHeader, "true");
        return client;
    }

    private async Task<Guid> SeedAsync(bool deadLettered)
    {
        var id = Guid.NewGuid();
        using (TenantScope.BeginHostScope())
        {
            await fixture.WithScopeAsync(async sp =>
            {
                var db = sp.GetRequiredService<SubscriptionsDbContext>();
                db.Set<OutboxMessage>().Add(new OutboxMessage
                {
                    Id = id,
                    MessageType = "unknown.e2e-event.v1",
                    Payload = "{}",
                    OccurredOn = DateTime.UtcNow,
                    NextAttemptAt = DateTime.UtcNow.AddDays(1), // never due: the sweep leaves it alone
                    Attempts = deadLettered ? 10 : 2,
                    DeadLetteredAt = deadLettered ? DateTime.UtcNow : null,
                    LastError = deadLettered ? "e2e boom" : null
                });
                await db.SaveChangesAsync();
            });
        }

        return id;
    }

    private async Task<OutboxMessage> RowAsync(Guid id)
    {
        using (TenantScope.BeginHostScope())
        {
            return await fixture.WithScopeAsync(sp => sp.GetRequiredService<SubscriptionsDbContext>()
                .Set<OutboxMessage>().AsNoTracking().SingleAsync(m => m.Id == id));
        }
    }

    [Theory]
    [InlineData(RoleConstants.Admin)]
    [InlineData(RoleConstants.Support)]
    [InlineData(RoleConstants.User)]
    public async Task Roles_without_outbox_manage_are_forbidden(string role)
    {
        var client = As(role);
        (await client.GetAsync(Base)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.PostAsync($"{Base}/{Guid.NewGuid()}/requeue", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.PostAsync($"{Base}/requeue", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Anonymous_is_unauthorized()
    {
        (await fixture.CreateAnonymousClient().GetAsync(Base)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task List_then_requeue_one_resets_the_row_and_requeueing_a_non_dead_letter_conflicts()
    {
        var client = As(RoleConstants.SuperAdmin);
        var dead = await SeedAsync(deadLettered: true);
        var pending = await SeedAsync(deadLettered: false);

        var page = (await client.GetFromJsonAsync<Envelope<Page>>(Base, Json))!.Data!;
        page.Result.Should().Contain(r => r.Id == dead && r.LastError == "e2e boom").And.NotContain(r => r.Id == pending);

        (await client.PostAsync($"{Base}/{dead}/requeue", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        var stored = await RowAsync(dead);
        stored.DeadLetteredAt.Should().BeNull();
        stored.Attempts.Should().Be(0);

        (await client.PostAsync($"{Base}/{dead}/requeue", null)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await client.PostAsync($"{Base}/{pending}/requeue", null)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await RowAsync(pending)).Attempts.Should().Be(2);
    }

    [Fact]
    public async Task Requeue_all_resets_dead_letters_and_unknown_targets_are_not_found()
    {
        var client = As(RoleConstants.SuperAdmin);
        var a = await SeedAsync(true);
        var b = await SeedAsync(true);

        var response = await client.PostAsync($"{Base}/requeue", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<Envelope<RequeueData>>(Json))!.Data!.Requeued
            .Should().BeGreaterThanOrEqualTo(2);
        (await RowAsync(a)).DeadLetteredAt.Should().BeNull();
        (await RowAsync(b)).DeadLetteredAt.Should().BeNull();

        (await client.PostAsync($"{Base}/{Guid.NewGuid()}/requeue", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync("/api/v1/admin/outbox/NopeDbContext/dead-letters")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
