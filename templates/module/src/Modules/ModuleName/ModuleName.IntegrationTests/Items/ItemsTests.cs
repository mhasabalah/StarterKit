using ModuleName.Contracts.Items;
using ModuleName.Domains.Items.Exceptions;
using Shared.Core.Exceptions;
#if (TENANT_DATA)
using Shared.Core.MultiTenancy;
#endif

namespace ModuleName.IntegrationTests.Items;

/// <summary>Every behavior and failure mode of the example aggregate, through the real mediator on real Postgres.</summary>
[Collection(ModuleNamePostgresCollection.Name)]
public class ItemsTests(ModuleNamePostgresFixture fx)
{
#if (TENANT_DATA)
    private static readonly TenantInfo TenantA = ModuleNamePostgresFixture.TenantA;
    private static readonly TenantInfo TenantB = ModuleNamePostgresFixture.TenantB;

    private Task<T> Send<T>(Shared.Contracts.Mediation.IRequest<T> request) => fx.SendAsync(TenantA, request);
#else
    private Task<T> Send<T>(Shared.Contracts.Mediation.IRequest<T> request) => fx.SendAsync(request);
#endif

    private static string NewCode() => "IT-" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();

    [Fact]
    public async Task Create_then_get_returns_the_item_with_its_version()
    {
        var code = NewCode();

        var created = await Send(new CreateItemCommand(code.ToLowerInvariant(), " Widget ", "A widget"));
        var item = await Send(new GetItemByIdQuery(created.Id));

        item.Code.Should().Be(code);
        item.Name.Should().Be("Widget");
        item.Description.Should().Be("A widget");
        item.CreatedAt.Should().NotBeNull();
        item.Version.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task Create_with_a_taken_code_is_a_conflict()
    {
        var code = NewCode();
        await Send(new CreateItemCommand(code, "First", null));

        var act = () => Send(new CreateItemCommand(code.ToLowerInvariant(), "Second", null));

        await act.Should().ThrowAsync<ItemAlreadyExistsException>();
    }

    [Fact]
    public async Task Create_with_an_invalid_command_is_rejected_by_validation()
    {
        var act = () => Send(new CreateItemCommand("bad code", "", null));

        await act.Should().ThrowAsync<FluentValidation.ValidationException>();
    }

    [Fact]
    public async Task Update_with_the_current_version_succeeds_and_returns_a_new_version()
    {
        var id = (await Send(new CreateItemCommand(NewCode(), "Before", null))).Id;
        var read = await Send(new GetItemByIdQuery(id));

        var updated = await Send(new UpdateItemCommand(id, "After", "Now described", read.Version));

        updated.Version.Should().BeGreaterThan(read.Version);
        (await Send(new GetItemByIdQuery(id))).Name.Should().Be("After");
    }

    [Fact]
    public async Task Update_WithAStaleVersion_IsRejected_AndNothingChanges()
    {
        var id = (await Send(new CreateItemCommand(NewCode(), "Original", null))).Id;
        var stale = (await Send(new GetItemByIdQuery(id))).Version;
        await Send(new UpdateItemCommand(id, "First writer", null, stale));

        var act = () => Send(new UpdateItemCommand(id, "Second writer", null, stale));

        await act.Should().ThrowAsync<ConcurrencyConflictException>();
        (await Send(new GetItemByIdQuery(id))).Name.Should().Be("First writer");
    }

    [Fact]
    public async Task Missing_item_is_NotFound_for_get_update_and_delete()
    {
        var missing = Guid.NewGuid();

        await FluentActions.Awaiting(() => Send(new GetItemByIdQuery(missing))).Should().ThrowAsync<NotFoundException>();
        await FluentActions.Awaiting(() => Send(new UpdateItemCommand(missing, "Name", null))).Should().ThrowAsync<NotFoundException>();
        await FluentActions.Awaiting(() => Send(new DeleteItemCommand(missing))).Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Delete_removes_the_item()
    {
        var id = (await Send(new CreateItemCommand(NewCode(), "Doomed", null))).Id;

        (await Send(new DeleteItemCommand(id))).Deleted.Should().BeTrue();

        await FluentActions.Awaiting(() => Send(new GetItemByIdQuery(id))).Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task List_pages_newest_first_and_searches_by_code_or_name()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];
        var older = await Send(new CreateItemCommand(NewCode(), $"Alpha {marker}", null));
        var newer = await Send(new CreateItemCommand(NewCode(), $"Beta {marker}", null));

        var page = await Send(new GetItemsQuery { Search = marker, PageNumber = 1, PageSize = 10 });

        page.Result!.Select(i => i.Id).Should().Equal(newer.Id, older.Id);
        page.PageInfoResult.TotalItems.Should().Be(2);

        var firstOnly = await Send(new GetItemsQuery { Search = marker, PageNumber = 1, PageSize = 1 });
        firstOnly.Result.Should().ContainSingle().Which.Id.Should().Be(newer.Id);
    }
#if (OUTBOX)

    [Fact]
    public async Task Create_writes_the_integration_event_to_the_outbox_in_the_same_transaction()
    {
        var id = (await Send(new CreateItemCommand(NewCode(), "Announced", null))).Id;

        (await fx.AdminScalarAsync<long>(
                $"select count(*) from modulename.outbox_messages where payload::text like '%{id}%'"))
            .Should().Be(1);
    }
#endif
#if (TENANT_DATA)

    [Fact]
    public async Task Another_tenant_cannot_see_change_or_delete_an_item_and_may_reuse_its_code()
    {
        var code = NewCode();
        var id = (await fx.SendAsync(TenantA, new CreateItemCommand(code, "Tenant A's", null))).Id;

        await FluentActions.Awaiting(() => fx.SendAsync(TenantB, new GetItemByIdQuery(id))).Should().ThrowAsync<NotFoundException>();
        await FluentActions.Awaiting(() => fx.SendAsync(TenantB, new UpdateItemCommand(id, "Hijacked", null))).Should().ThrowAsync<NotFoundException>();
        await FluentActions.Awaiting(() => fx.SendAsync(TenantB, new DeleteItemCommand(id))).Should().ThrowAsync<NotFoundException>();
        (await fx.SendAsync(TenantB, new GetItemsQuery { Search = code })).Result.Should().BeEmpty();

        // Codes are unique per tenant, not globally.
        await fx.SendAsync(TenantB, new CreateItemCommand(code, "Tenant B's", null));
        (await fx.SendAsync(TenantA, new GetItemByIdQuery(id))).Name.Should().Be("Tenant A's");
    }

    [Fact]
    public async Task Every_table_has_forced_row_level_security_and_the_tenant_policy()
    {
        var unprotected = await fx.AdminScalarAsync<long>(
            "select count(*) from pg_class c join pg_namespace n on n.oid = c.relnamespace " +
            "where n.nspname = 'modulename' and c.relkind = 'r' and c.relname not like '\\_\\_%' " +
            "and (not c.relrowsecurity or not c.relforcerowsecurity " +
            "or not exists (select 1 from pg_policies p where p.schemaname = n.nspname and p.tablename = c.relname " +
            "and p.policyname = 'tenant_isolation'))");

        unprotected.Should().Be(0);
    }
#endif
}
