using Microsoft.EntityFrameworkCore;
using Shared.Core.Data;

namespace Shared.Core.Tests.Concurrency;

/// <summary>
/// <c>Aggregate.BumpVersionOnSave</c> on real PostgreSQL: a child-only change updates and checks the root row when the
/// aggregate asks for it (ADR 0021).
/// </summary>
[Collection(PostgresConcurrencyCollection.Name)]
public sealed class ChildCollectionVersionTests(PostgresConcurrencyFixture fx)
{
    private async Task<Guid> SeedWithPartAsync(params string[] parts)
    {
        var widget = await fx.SeedAsync();
        await using var db = fx.CreateContext();
        var tracked = await db.Widgets.Include(w => w.Parts).SingleAsync(w => w.Id == widget.Id);
        foreach (var part in parts)
            tracked.AddPartUnversioned(part);
        await db.SaveChangesAsync();
        return widget.Id;
    }

    [Fact]
    public async Task Requested_bump_moves_the_root_version_when_only_a_child_was_added()
    {
        var id = await SeedWithPartAsync();

        await using (var db = fx.CreateContext())
        {
            var widget = await db.Widgets.Include(w => w.Parts).SingleAsync(w => w.Id == id);
            widget.AddPartVersioned("a");
            await db.SaveChangesAsync();
        }

        (await fx.ReloadAsync(id)).Version.Should().Be(1);
    }

    [Fact]
    public async Task Requested_bump_moves_the_root_version_when_only_a_child_was_removed()
    {
        var id = await SeedWithPartAsync("a");

        await using (var db = fx.CreateContext())
        {
            var widget = await db.Widgets.Include(w => w.Parts).SingleAsync(w => w.Id == id);
            widget.RemovePartVersioned("a");
            await db.SaveChangesAsync();
        }

        (await fx.ReloadAsync(id)).Version.Should().Be(1);
    }

    [Fact]
    public async Task Without_a_request_a_child_only_change_leaves_the_root_version_alone()
    {
        var id = await SeedWithPartAsync("a");

        (await fx.ReloadAsync(id)).Version.Should().Be(0);
    }

    [Fact]
    public async Task Two_concurrent_child_changes_conflict_and_only_the_first_is_kept()
    {
        var id = await SeedWithPartAsync();

        await using var first = fx.CreateContext();
        await using var second = fx.CreateContext();
        var a = await first.Widgets.Include(w => w.Parts).SingleAsync(w => w.Id == id);
        var b = await second.Widgets.Include(w => w.Parts).SingleAsync(w => w.Id == id);

        a.AddPartVersioned("first");
        await first.SaveChangesAsync();

        b.AddPartVersioned("second");
        var save = () => second.SaveChangesAsync();

        await save.Should().ThrowAsync<DbUpdateConcurrencyException>();
        await using var check = fx.CreateContext();
        var stored = await check.Widgets.AsNoTracking().Include(w => w.Parts).SingleAsync(w => w.Id == id);
        stored.Parts.Select(p => p.Name).Should().Equal("first");
        stored.Version.Should().Be(1);
    }

    [Fact]
    public async Task A_stale_client_version_is_rejected_after_a_child_only_change()
    {
        var id = await SeedWithPartAsync();
        await using (var db = fx.CreateContext())
        {
            var widget = await db.Widgets.Include(w => w.Parts).SingleAsync(w => w.Id == id);
            widget.AddPartVersioned("a");
            await db.SaveChangesAsync();
        }

        await using var stale = fx.CreateContext();
        var loaded = await stale.Widgets.SingleAsync(w => w.Id == id);
        stale.ExpectVersion(loaded, expectedVersion: 0);
        loaded.Rename("lost update");
        var save = () => stale.SaveChangesAsync();

        await save.Should().ThrowAsync<DbUpdateConcurrencyException>();
    }

    [Fact]
    public async Task A_request_made_on_a_new_aggregate_does_not_bump_a_later_save()
    {
        await using var db = fx.CreateContext();
        var widget = Widget.Create("new", 1, 1);
        widget.AddPartVersioned("a");
        db.Widgets.Add(widget);
        await db.SaveChangesAsync();

        await db.SaveChangesAsync();

        (await fx.ReloadAsync(widget.Id)).Version.Should().Be(0);
    }

    [Fact]
    public async Task A_request_on_an_aggregate_being_deleted_still_deletes_it()
    {
        var id = await SeedWithPartAsync("a");

        await using (var db = fx.CreateContext())
        {
            var widget = await db.Widgets.Include(w => w.Parts).SingleAsync(w => w.Id == id);
            widget.RemovePartVersioned("a");
            db.Widgets.Remove(widget);
            await db.SaveChangesAsync();
        }

        await using var check = fx.CreateContext();
        (await check.Widgets.AnyAsync(w => w.Id == id)).Should().BeFalse();
    }
}
