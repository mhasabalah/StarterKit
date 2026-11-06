using Catalog.Data.Configurations;
using Catalog.Domains.Categories.Exceptions;

namespace Catalog.Data.Repository;

public interface ICategoryRepository
{
    Task<Category?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default);

    Task<bool> SlugExistsAsync(string normalizedSlug, Guid? excludeCategoryId = null,
        CancellationToken cancellationToken = default);

    /// <summary>(Id → ParentId) for every category — input for cycle detection.</summary>
    Task<IReadOnlyDictionary<Guid, Guid?>> GetParentMapAsync(CancellationToken cancellationToken = default);

    /// <summary>Bulk import: the tracked categories with these normalized slugs (no children, no tracking of others).</summary>
    Task<List<Category>> GetBySlugsAsync(IReadOnlyCollection<string> normalizedSlugs,
        CancellationToken cancellationToken = default);

    /// <summary>Bulk import: tracks a new category; it is inserted by the next <see cref="SaveChangesAsync"/>.</summary>
    void Add(Category category);

    Task AddAsync(Category category, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public class CategoryRepository(CatalogDbContext context) : ICategoryRepository
{
    public Task<Category?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.Categories.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.Categories.AnyAsync(c => c.Id == id, cancellationToken);

    public Task<bool> SlugExistsAsync(
        string normalizedSlug, Guid? excludeCategoryId = null, CancellationToken cancellationToken = default) =>
        context.Categories.AsNoTracking()
            .AnyAsync(c => c.Slug == normalizedSlug && (excludeCategoryId == null || c.Id != excludeCategoryId),
                cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, Guid?>> GetParentMapAsync(CancellationToken cancellationToken = default) =>
        await context.Categories.AsNoTracking()
            .Select(c => new { c.Id, c.ParentId })
            .ToDictionaryAsync(c => c.Id, c => c.ParentId, cancellationToken);

    public Task<List<Category>> GetBySlugsAsync(IReadOnlyCollection<string> normalizedSlugs,
        CancellationToken cancellationToken = default) =>
        context.Categories.Where(c => normalizedSlugs.Contains(c.Slug)).ToListAsync(cancellationToken);

    public void Add(Category category) => context.Categories.Add(category);

    public async Task AddAsync(Category category, CancellationToken cancellationToken = default)
    {
        await context.Categories.AddAsync(category, cancellationToken);
        await SaveChangesAsync(cancellationToken);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation(CategoryConfiguration.SlugIndexName))
        {
            var slug = context.ChangeTracker.Entries<Category>().Select(e => e.Entity.Slug).FirstOrDefault();
            throw new CategorySlugAlreadyExistsException(slug ?? string.Empty);
        }
    }
}
