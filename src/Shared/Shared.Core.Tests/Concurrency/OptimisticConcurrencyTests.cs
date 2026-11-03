using Microsoft.EntityFrameworkCore;
using Shared.Core.Data;
using Shared.Core.Exceptions;

namespace Shared.Core.Tests.Concurrency;

/// <summary>
/// Tests optimistic concurrency end to end against real PostgreSQL:
/// <c>AuditableEntityInterceptor</c> + <c>ConfigureVersionConcurrencyTokens</c> + <c>ExpectVersion</c>.
/// <para>
/// Regression guard: an earlier interceptor overwrote <c>Version.OriginalValue</c> with the loaded value on
/// every save. Stale client versions were then silently accepted ("last write wins") instead of
/// returning 409. <see cref="ExpectVersion_with_stale_client_version_is_rejected"/> fails if that comes back.
/// </para>
/// </summary>
[Collection(PostgresConcurrencyCollection.Name)]
public sealed class OptimisticConcurrencyTests(PostgresConcurrencyFixture fx)
{
    [Fact]
    public async Task Insert_starts_at_version_zero_and_stamps_audit_fields()
    {
        var widget = await fx.SeedAsync();

        var stored = await fx.ReloadAsync(widget.Id);

        stored.Version.Should().Be(0);
        stored.CreatedBy.Should().Be(PostgresConcurrencyFixture.CurrentUserId);
        stored.CreatedAt.Should().NotBeNull();
        stored.LastModifiedBy.Should().Be(PostgresConcurrencyFixture.CurrentUserId);
    }

    [Fact]
    public async Task Update_increments_version_by_exactly_one()
    {
        var widget = await fx.SeedAsync();

        await using (var db = fx.CreateContext())
        {
            var w = await db.Widgets.SingleAsync(x => x.Id == widget.Id);
            w.Rename("renamed");
            await db.SaveChangesAsync();
        }

        var stored = await fx.ReloadAsync(widget.Id);
        stored.Version.Should().Be(1);
        stored.Name.Should().Be("renamed");
    }

    [Fact]
    public async Task ExpectVersion_with_current_client_version_succeeds()
    {
        var widget = await fx.SeedAsync();

        await using (var db = fx.CreateContext())
        {
            var w = await db.Widgets.SingleAsync(x => x.Id == widget.Id);
            db.ExpectVersion(w, expectedVersion: 0);
            w.Rename("ok");
            await db.SaveChangesAsync();
        }

        (await fx.ReloadAsync(widget.Id)).Version.Should().Be(1);
    }

    [Fact]
    public async Task ExpectVersion_with_stale_client_version_is_rejected()
    {
        // Row is at version 2; the client still holds version 1.
        var widget = await fx.SeedAsync();
        await RenameAsync(widget.Id, "v1");
        await RenameAsync(widget.Id, "v2");

        await using var db = fx.CreateContext();
        var w = await db.Widgets.SingleAsync(x => x.Id == widget.Id);
        db.ExpectVersion(w, expectedVersion: 1);
        w.Rename("lost update");

        var save = () => db.SaveChangesAsync();

        await ShouldConflictAsync(save);
        var stored = await fx.ReloadAsync(widget.Id);
        stored.Name.Should().Be("v2", "a stale write must not overwrite newer data");
        stored.Version.Should().Be(2);
    }

    [Fact]
    public async Task ExpectVersion_with_future_client_version_is_rejected()
    {
        var widget = await fx.SeedAsync();

        await using var db = fx.CreateContext();
        var w = await db.Widgets.SingleAsync(x => x.Id == widget.Id);
        db.ExpectVersion(w, expectedVersion: 99);
        w.Rename("bogus");

        var save = () => db.SaveChangesAsync();

        await ShouldConflictAsync(save);
    }

    [Fact]
    public async Task ExpectVersion_null_falls_back_to_loaded_version()
    {
        var widget = await fx.SeedAsync();
        await RenameAsync(widget.Id, "v1");

        await using (var db = fx.CreateContext())
        {
            var w = await db.Widgets.SingleAsync(x => x.Id == widget.Id);
            db.ExpectVersion(w, expectedVersion: null);
            w.Rename("v2");
            await db.SaveChangesAsync();
        }

        (await fx.ReloadAsync(widget.Id)).Version.Should().Be(2);
    }

    [Fact]
    public async Task Racing_requests_second_writer_gets_conflict_even_without_client_version()
    {
        var widget = await fx.SeedAsync();

        await using var requestA = fx.CreateContext();
        await using var requestB = fx.CreateContext();
        var a = await requestA.Widgets.SingleAsync(x => x.Id == widget.Id);
        var b = await requestB.Widgets.SingleAsync(x => x.Id == widget.Id);

        b.Rename("B wins");
        await requestB.SaveChangesAsync();

        a.Rename("A loses");
        var save = () => requestA.SaveChangesAsync();

        await ShouldConflictAsync(save);
        (await fx.ReloadAsync(widget.Id)).Name.Should().Be("B wins");
    }

    [Fact]
    public async Task Two_saves_in_the_same_scope_do_not_cause_a_spurious_conflict()
    {
        var widget = await fx.SeedAsync();

        await using (var db = fx.CreateContext())
        {
            var w = await db.Widgets.SingleAsync(x => x.Id == widget.Id);
            db.ExpectVersion(w, 0);

            w.Rename("first");
            await db.SaveChangesAsync();

            w.Rename("second");
            await db.SaveChangesAsync();
        }

        var stored = await fx.ReloadAsync(widget.Id);
        stored.Name.Should().Be("second");
        stored.Version.Should().Be(2);
    }

    [Fact]
    public async Task Owned_value_object_only_change_bumps_aggregate_version()
    {
        var widget = await fx.SeedAsync(width: 1, height: 1);

        await using (var db = fx.CreateContext())
        {
            var w = await db.Widgets.SingleAsync(x => x.Id == widget.Id);
            w.Resize(10, 20);
            await db.SaveChangesAsync();
        }

        var stored = await fx.ReloadAsync(widget.Id);
        stored.Size.Should().Be(new Dimensions(10, 20));
        stored.Version.Should().Be(1);
    }

    [Fact]
    public async Task Owned_value_object_only_change_is_concurrency_checked()
    {
        var widget = await fx.SeedAsync();

        await using var requestA = fx.CreateContext();
        await using var requestB = fx.CreateContext();
        var a = await requestA.Widgets.SingleAsync(x => x.Id == widget.Id);
        var b = await requestB.Widgets.SingleAsync(x => x.Id == widget.Id);

        b.Rename("B");
        await requestB.SaveChangesAsync();

        a.Resize(5, 5);
        var save = () => requestA.SaveChangesAsync();

        await ShouldConflictAsync(save);
    }

    [Fact]
    public async Task Unchanged_entity_is_not_updated_and_keeps_its_version()
    {
        var widget = await fx.SeedAsync();

        await using (var db = fx.CreateContext())
        {
            _ = await db.Widgets.SingleAsync(x => x.Id == widget.Id);
            (await db.SaveChangesAsync()).Should().Be(0);
        }

        (await fx.ReloadAsync(widget.Id)).Version.Should().Be(0);
    }

    [Fact]
    public async Task ExpectVersion_rejects_untracked_or_new_entities()
    {
        await using var db = fx.CreateContext();
        var detached = Widget.Create("detached", 1, 1);

        var onDetached = () => db.ExpectVersion(detached, 0);
        onDetached.Should().Throw<InvalidOperationException>();

        db.Widgets.Add(detached);
        var onAdded = () => db.ExpectVersion(detached, 0);
        onAdded.Should().Throw<InvalidOperationException>();
    }

    /// <summary>
    /// The DbContext throws EF's DbUpdateConcurrencyException UNCHANGED (AppDbContextBase no longer translates it), so
    /// jobs can catch it per item. The single translation to ConcurrencyConflictException (409) is
    /// ConcurrencyHandlingBehavior - see ConcurrencyHandlingBehaviorTests and the E2E stale-version tests.
    /// </summary>
    private static async Task ShouldConflictAsync(Func<Task<int>> save)
    {
        var thrown = await save.Should().ThrowAsync<DbUpdateConcurrencyException>();
        thrown.Which.IsConcurrencyConflict().Should().BeTrue();
    }

    private async Task RenameAsync(Guid id, string name)
    {
        await using var db = fx.CreateContext();
        var w = await db.Widgets.SingleAsync(x => x.Id == id);
        w.Rename(name);
        await db.SaveChangesAsync();
    }
}
