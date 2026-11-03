using Microsoft.EntityFrameworkCore;
using Shared.Core.MultiTenancy;
using Shared.Core.MultiTenancy.Data;
using static Shared.Core.Tests.MultiTenancy.TenantTestData;

namespace Shared.Core.Tests.MultiTenancy;

/// <summary>
/// The EF layer of tenant isolation: named Tenant/Branch/SoftDelete filters and the write interceptor
/// (EF in-memory provider - no Docker; RLS is covered by the Postgres-based tests).
/// </summary>
public sealed class TenantDataIsolationTests
{
    private readonly string _db = Guid.NewGuid().ToString();
    private readonly ITenantContext _context = MultiTenantContext();

    [Fact]
    public async Task Each_tenant_sees_only_its_own_rows()
    {
        await SeedAsync(_db, _context, TenantA, Note("a1"), Note("a2"));
        await SeedAsync(_db, _context, TenantB, Note("b1"));

        using (TenantScope.Begin(TenantA))
        {
            await using var ctx = NotesDbContext.Create(_db, _context);
            (await ctx.Notes.Select(n => n.Text).ToListAsync()).Should().BeEquivalentTo("a1", "a2");
        }

        using (TenantScope.Begin(TenantB))
        {
            await using var ctx = NotesDbContext.Create(_db, _context);
            (await ctx.Notes.Select(n => n.Text).ToListAsync()).Should().BeEquivalentTo("b1");
        }
    }

    [Fact]
    public async Task Inserts_are_stamped_with_the_current_tenant()
    {
        await SeedAsync(_db, _context, TenantA, Note("a1"));

        using (TenantScope.BeginHostScope())
        {
            await using var ctx = NotesDbContext.Create(_db, _context);
            (await ctx.Notes.SingleAsync()).TenantId.Should().Be(TenantA.Id);
        }
    }

    [Fact]
    public async Task No_tenant_means_no_rows_and_no_writes()
    {
        await SeedAsync(_db, _context, TenantA, Note("a1"));

        using (TenantScope.BeginEmpty())
        {
            await using var ctx = NotesDbContext.Create(_db, _context);
            (await ctx.Notes.ToListAsync()).Should().BeEmpty("an unresolved tenant matches nothing (fail closed)");

            ctx.Notes.Add(Note("orphan"));
            await FluentActions.Awaiting(() => ctx.SaveChangesAsync()).Should().ThrowAsync<TenantNotResolvedException>();
        }
    }

    [Fact]
    public async Task Host_scope_sees_every_tenant()
    {
        await SeedAsync(_db, _context, TenantA, Note("a1"));
        await SeedAsync(_db, _context, TenantB, Note("b1"));

        using (TenantScope.BeginHostScope())
        {
            await using var ctx = NotesDbContext.Create(_db, _context);
            (await ctx.Notes.CountAsync()).Should().Be(2);
        }
    }

    [Fact]
    public async Task Ignoring_non_isolation_filters_keeps_tenant_isolation()
    {
        await SeedAsync(_db, _context, TenantA, Note("a-deleted", deleted: true), Note("a-live"));
        await SeedAsync(_db, _context, TenantB, Note("b-deleted", deleted: true));

        using (TenantScope.Begin(TenantA))
        {
            await using var ctx = NotesDbContext.Create(_db, _context);
            (await ctx.Notes.Select(n => n.Text).ToListAsync()).Should().BeEquivalentTo("a-live");
            (await ctx.Notes.IgnoreNonIsolationFilters().Select(n => n.Text).ToListAsync())
                .Should().BeEquivalentTo(["a-deleted", "a-live"], "soft-deleted rows return, other tenants do not");
        }
    }

    [Fact]
    public async Task Updating_another_tenants_row_is_blocked()
    {
        var foreign = Note("b1");
        await SeedAsync(_db, _context, TenantB, foreign);

        using (TenantScope.Begin(TenantA))
        {
            await using var ctx = NotesDbContext.Create(_db, _context);
            // Attach-and-update of a foreign id (e.g. a forged id in a request body) must fail on save.
            var forged = new Note { Id = foreign.Id, TenantId = TenantB.Id, Text = "hacked" };
            ctx.Notes.Update(forged);

            await FluentActions.Awaiting(() => ctx.SaveChangesAsync()).Should().ThrowAsync<CrossTenantAccessException>();
        }
    }

    [Fact]
    public async Task Inserting_into_another_tenant_is_blocked()
    {
        using (TenantScope.Begin(TenantA))
        {
            await using var ctx = NotesDbContext.Create(_db, _context);
            var note = Note("x");
            note.TenantId = TenantB.Id;
            ctx.Notes.Add(note);

            await FluentActions.Awaiting(() => ctx.SaveChangesAsync()).Should().ThrowAsync<CrossTenantAccessException>();
        }
    }

    [Fact]
    public async Task TenantId_can_never_change()
    {
        var note = Note("a1");
        await SeedAsync(_db, _context, TenantA, note);

        using (TenantScope.BeginHostScope())
        {
            await using var ctx = NotesDbContext.Create(_db, _context);
            var row = await ctx.Notes.SingleAsync(n => n.Id == note.Id);
            row.TenantId = TenantB.Id;

            await FluentActions.Awaiting(() => ctx.SaveChangesAsync()).Should().ThrowAsync<CrossTenantAccessException>();
        }
    }

    [Fact]
    public async Task Branch_scope_limits_rows_but_keeps_tenant_wide_rows()
    {
        var cairo = Guid.NewGuid();
        var giza = Guid.NewGuid();
        await SeedAsync(_db, _context, TenantA, Note("cairo", cairo), Note("giza", giza), Note("tenant-wide"));

        using (TenantScope.Begin(TenantA, new BranchScope([cairo])))
        {
            await using var ctx = NotesDbContext.Create(_db, _context);
            (await ctx.Notes.Select(n => n.Text).ToListAsync()).Should().BeEquivalentTo("cairo", "tenant-wide");
            (await ctx.Notes.IgnoreBranchScope().CountAsync()).Should().Be(3, "IgnoreBranchScope keeps only tenant isolation");
        }

        using (TenantScope.Begin(TenantA, BranchScope.Unrestricted))
        {
            await using var ctx = NotesDbContext.Create(_db, _context);
            (await ctx.Notes.CountAsync()).Should().Be(3);
        }
    }

    [Fact]
    public async Task Single_tenant_mode_uses_the_default_tenant_without_any_scope()
    {
        var context = TenantContext.SingleTenant;
        await using (var ctx = NotesDbContext.Create(_db, context))
        {
            ctx.Notes.Add(Note("x"));
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = NotesDbContext.Create(_db, context))
        {
            var row = await ctx.Notes.SingleAsync();
            row.TenantId.Should().Be(TenantDefaults.DefaultTenantId);
        }
    }

    [Fact]
    public void Purge_order_deletes_dependents_first()
    {
        using var ctx = NotesDbContext.Create(_db, _context);
        TenantDataPurger.TenantTablesInDeleteOrder(ctx.Model).Should().ContainSingle();
    }
}
