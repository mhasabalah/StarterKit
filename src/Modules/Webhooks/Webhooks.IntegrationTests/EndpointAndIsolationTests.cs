using FluentValidation;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Shared.Core.Exceptions;
using Webhooks.Contracts.Deliveries;
using Webhooks.Contracts.Endpoints;
using Webhooks.Domains.Endpoints.Models;
using Webhooks.Security;
using static Webhooks.IntegrationTests.WebhooksPostgresFixture;

namespace Webhooks.IntegrationTests;

[Collection(WebhooksPostgresCollection.Name)]
public class EndpointAndIsolationTests(WebhooksPostgresFixture fx)
{
    [Fact]
    public async Task Create_ReturnsTheSecretOnce_AndStoresItEncrypted()
    {
        var (id, secret) = await Arrange.Endpoint(fx, TenantA);

        secret.Should().StartWith("whsec_");
        var row = await Arrange.EndpointRow(fx, TenantA, id);
        row.SecretProtected.Should().NotContain(secret).And.NotBe(secret);
        using var scope = fx.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IWebhookSecretProtector>().Unprotect(row.SecretProtected).Should().Be(secret);

        var dto = await fx.SendAsync(TenantA, new GetWebhookEndpointByIdQuery(id));
        dto.GetType().GetProperties().Select(p => p.Name).Should().NotContain(n => n.Contains("Secret"));
    }

    [Fact]
    public async Task RotateSecret_IssuesANewSecret_AndTheOldOneNoLongerMatches()
    {
        var (id, secret) = await Arrange.Endpoint(fx, TenantA);
        var before = (await Arrange.EndpointRow(fx, TenantA, id)).SecretProtected;

        var rotated = await fx.SendAsync(TenantA, new RotateWebhookEndpointSecretCommand(id));

        rotated.Secret.Should().NotBe(secret).And.StartWith("whsec_");
        (await Arrange.EndpointRow(fx, TenantA, id)).SecretProtected.Should().NotBe(before);
    }

    [Fact]
    public async Task Create_WithADuplicateUrl_IsAConflict()
    {
        var url = $"http://127.0.0.1:1/{Guid.NewGuid():N}";
        await Arrange.Endpoint(fx, TenantA, url);

        var act = () => Arrange.Endpoint(fx, TenantA, url);

        await act.Should().ThrowAsync<ConflictException>();
        // The same URL in another tenant is fine (unique per tenant).
        await Arrange.Endpoint(fx, TenantB, url);
    }

    [Fact]
    public async Task Create_WithANonHttpUrl_IsRejectedByValidation()
    {
        var act = () => Arrange.Endpoint(fx, TenantA, "ftp://hooks.example.com");

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Create_BeyondTheEndpointCap_IsRejected()
    {
        var limit = fx.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<Webhooks.Options.WebhooksOptions>>().Value;
        var original = limit.MaxEndpointsPerTenant;
        var tenant = new Shared.Core.MultiTenancy.TenantInfo(Guid.Parse("cccccccc-3333-0000-0000-000000000003"), "capco", "Cap Co");
        try
        {
            limit.MaxEndpointsPerTenant = 1;
            await Arrange.Endpoint(fx, tenant);

            var act = () => Arrange.Endpoint(fx, tenant);

            await act.Should().ThrowAsync<BadRequestException>();
        }
        finally
        {
            limit.MaxEndpointsPerTenant = original;
        }
    }

    [Fact]
    public async Task Create_WithAnUnknownEventType_IsRejected()
    {
        var act = () => fx.SendAsync(TenantA, new CreateWebhookEndpointCommand("https://hooks.example.com/x", null, ["made.up.v1"]));

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Update_WithAStaleVersion_IsRejected_AndNothingChanges()
    {
        var (id, _) = await Arrange.Endpoint(fx, TenantA);
        var current = await fx.SendAsync(TenantA, new GetWebhookEndpointByIdQuery(id));
        await fx.SendAsync(TenantA, new UpdateWebhookEndpointCommand(id, current.Url, "first", current.EventTypes, true, current.Version));

        var act = () => fx.SendAsync(TenantA, new UpdateWebhookEndpointCommand(id, current.Url, "stale", current.EventTypes, true, current.Version));

        await act.Should().ThrowAsync<ConcurrencyConflictException>();
        (await Arrange.EndpointRow(fx, TenantA, id)).Description.Should().Be("first");
    }

    [Fact]
    public async Task Update_ReEnabling_ResetsFailuresAndDisabledState()
    {
        var (id, _) = await Arrange.Endpoint(fx, TenantA);
        await fx.AdminExecAsync($"update webhooks.webhook_endpoints set enabled=false, consecutive_failures=10, disabled_at=now() where id='{id}'");
        var current = await fx.SendAsync(TenantA, new GetWebhookEndpointByIdQuery(id));
        current.Enabled.Should().BeFalse();

        await fx.SendAsync(TenantA, new UpdateWebhookEndpointCommand(id, current.Url, null, current.EventTypes, true, current.Version));

        var row = await Arrange.EndpointRow(fx, TenantA, id);
        row.Enabled.Should().BeTrue();
        row.ConsecutiveFailures.Should().Be(0);
        row.DisabledAt.Should().BeNull();
    }

    [Fact]
    public async Task Delete_RemovesTheEndpoint_AndItsDeliveries()
    {
        var (id, _) = await Arrange.Endpoint(fx, TenantA);
        await Arrange.OneDelivery(fx, TenantA, id);

        await fx.SendAsync(TenantA, new DeleteWebhookEndpointCommand(id));

        (await fx.AdminScalarAsync<long>($"select count(*) from webhooks.webhook_endpoints where id='{id}'")).Should().Be(0);
        (await fx.AdminScalarAsync<long>($"select count(*) from webhooks.webhook_deliveries where endpoint_id='{id}'")).Should().Be(0);
    }

    [Fact]
    public async Task Missing_endpoint_is_NotFound_for_every_operation()
    {
        var id = Guid.NewGuid();
        var ops = new Func<Task>[]
        {
            () => fx.SendAsync(TenantA, new GetWebhookEndpointByIdQuery(id)),
            () => fx.SendAsync(TenantA, new DeleteWebhookEndpointCommand(id)),
            () => fx.SendAsync(TenantA, new RotateWebhookEndpointSecretCommand(id)),
            () => fx.SendAsync(TenantA, new SendWebhookTestPingCommand(id)),
            () => fx.SendAsync(TenantA, new UpdateWebhookEndpointCommand(id, "https://a.example.com", null, ["*"], true))
        };

        foreach (var op in ops) await op.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task EventTypes_lists_the_public_catalog()
    {
        var types = await fx.SendAsync(TenantA, new GetWebhookEventTypesQuery());

        types.Select(t => t.Type).Should().BeEquivalentTo(Arrange.ProductPublished, "subscriptions.subscription-activated.v1",
            Arrange.SubscriptionExpired);
    }

    // ----- cross-tenant isolation -----------------------------------------------------------------------------------

    [Fact]
    public async Task Another_tenant_cannot_see_read_change_or_delete_an_endpoint()
    {
        var (id, _) = await Arrange.Endpoint(fx, TenantA);
        var delivery = await Arrange.OneDelivery(fx, TenantA, id);

        (await fx.SendAsync(TenantB, new GetWebhookEndpointsQuery { PageSize = 100 })).Result!.Should().NotContain(e => e.Id == id);
        (await fx.SendAsync(TenantB, new GetWebhookDeliveriesQuery { PageSize = 100 })).Result!.Should().NotContain(d => d.Id == delivery.Id);

        var attempts = new Func<Task>[]
        {
            () => fx.SendAsync(TenantB, new GetWebhookEndpointByIdQuery(id)),
            () => fx.SendAsync(TenantB, new DeleteWebhookEndpointCommand(id)),
            () => fx.SendAsync(TenantB, new RotateWebhookEndpointSecretCommand(id)),
            () => fx.SendAsync(TenantB, new SendWebhookTestPingCommand(id)),
            () => fx.SendAsync(TenantB, new UpdateWebhookEndpointCommand(id, "https://evil.example.com", null, ["*"], true)),
            () => fx.SendAsync(TenantB, new GetWebhookDeliveryByIdQuery(delivery.Id)),
            () => fx.SendAsync(TenantB, new RedeliverWebhookCommand(delivery.Id))
        };
        foreach (var attempt in attempts) await attempt.Should().ThrowAsync<NotFoundException>();

        (await Arrange.EndpointRow(fx, TenantA, id)).Url.Should().NotContain("evil");
    }

    [Fact]
    public async Task Row_level_security_hides_rows_even_without_the_ef_filters()
    {
        var (id, _) = await Arrange.Endpoint(fx, TenantA);

        var seenByB = await fx.InTenantDbAsync(TenantB, db => db.Database
            .SqlQuery<long>($"select count(*)::bigint as \"Value\" from webhooks.webhook_endpoints where id = {id}").SingleAsync());
        var seenByA = await fx.InTenantDbAsync(TenantA, db => db.Database
            .SqlQuery<long>($"select count(*)::bigint as \"Value\" from webhooks.webhook_endpoints where id = {id}").SingleAsync());

        seenByB.Should().Be(0, "forced RLS must hide tenant A's row from tenant B at the database level");
        seenByA.Should().Be(1);
    }

    [Fact]
    public async Task Every_table_has_forced_row_level_security_and_the_tenant_policy()
    {
        var tables = new[] { "webhook_endpoints", "webhook_deliveries" };
        foreach (var table in tables)
        {
            (await fx.AdminScalarAsync<bool>(
                $"select relforcerowsecurity from pg_class c join pg_namespace n on n.oid=c.relnamespace where n.nspname='webhooks' and c.relname='{table}'"))
                .Should().BeTrue(table);
            (await fx.AdminScalarAsync<long>(
                $"select count(*) from pg_policies where schemaname='webhooks' and tablename='{table}' and policyname='tenant_isolation'"))
                .Should().Be(1, table);
        }
    }

    [Fact]
    public async Task A_tenant_cannot_write_a_row_for_another_tenant()
    {
        var act = () => fx.InTenantDbAsync(TenantB, async db =>
        {
            var endpoint = WebhookEndpoint.Create("https://x.example.com/" + Guid.NewGuid(), null, ["*"], "s");
            db.Endpoints.Add(endpoint);
            await db.SaveChangesAsync();
            // Forge the owner: the interceptor / RLS must refuse to move it to tenant A.
            await db.Database.ExecuteSqlRawAsync(
                $"update webhooks.webhook_endpoints set tenant_id = '{TenantA.Id}' where id = '{endpoint.Id}'");
            return 0;
        });

        await act.Should().ThrowAsync<Exception>();
    }
}
