using MsOptions = Microsoft.Extensions.Options.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shared.Core.Data;
using Shared.Core.DDD;
using Shared.Core.MultiTenancy;
using Shared.Core.MultiTenancy.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Shared.Core.Tests.MultiTenancy;

/// <summary>Tenant-owned, branch-scoped, soft-deletable test entity.</summary>
public sealed class Note : Aggregate<Guid>, IMustHaveTenant, IBranchScoped
{
    public Guid TenantId { get; set; }
    public Guid? BranchId { get; set; }
    public string Text { get; set; } = string.Empty;
}

/// <summary>A minimal tenant-data context wired like the real ones (named filters + interceptors).</summary>
public sealed class NotesDbContext(DbContextOptions<NotesDbContext> options) : AppDbContextBase(options), ITenantDataContext
{
    public DbSet<Note> Notes => Set<Note>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<Note>().HasKey(n => n.Id);
        builder.FilterSoftDeletedProperties();
        ApplyTenantFilters(builder);
    }

    /// <summary>One in-memory database per <paramref name="name"/>; tenant context = the ambient TenantScope.</summary>
    public static NotesDbContext Create(string name, ITenantContext? tenantContext = null)
    {
        tenantContext ??= Shared.Core.MultiTenancy.TenantContext.SingleTenant;
        var services = new ServiceCollection().AddSingleton(tenantContext).BuildServiceProvider();

        var options = new DbContextOptionsBuilder<NotesDbContext>()
            .UseInMemoryDatabase(name)
            .UseApplicationServiceProvider(services)
            .AddInterceptors(new TenantEntityInterceptor(tenantContext, NullLogger<TenantEntityInterceptor>.Instance))
            .Options;

        return new NotesDbContext(options);
    }
}

public static class TenantTestData
{
    public static readonly TenantInfo TenantA = new(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"), "acme", "Acme");
    public static readonly TenantInfo TenantB = new(Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002"), "globex", "Globex");

    public static ITenantContext MultiTenantContext() =>
        new TenantContext(MsOptions.Create(new MultiTenancyOptions { Enabled = true }));

    public static async Task SeedAsync(string db, ITenantContext context, TenantInfo tenant, params Note[] notes)
    {
        using (TenantScope.Begin(tenant))
        {
            await using var ctx = NotesDbContext.Create(db, context);
            ctx.Notes.AddRange(notes);
            await ctx.SaveChangesAsync();
        }
    }

    public static Note Note(string text, Guid? branchId = null, bool deleted = false) =>
        new() { Id = Guid.NewGuid(), Text = text, BranchId = branchId, IsDeleted = deleted };
}
