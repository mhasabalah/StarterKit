using ModuleName.Domains.Items.Models;

namespace ModuleName.Data.Repository;

/// <summary>Write-side access to the Item aggregate (command handlers). Queries use the DbContext directly.</summary>
public interface IItemRepository
{
    Task<Item?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<bool> CodeExistsAsync(string normalizedCode, CancellationToken cancellationToken = default);

    void Add(Item item);

    void Remove(Item item);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public class ItemRepository(ModuleNameDbContext context) : IItemRepository
{
    public Task<Item?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.Items.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<bool> CodeExistsAsync(string normalizedCode, CancellationToken cancellationToken = default) =>
        context.Items.AsNoTracking().AnyAsync(x => x.Code == normalizedCode, cancellationToken);

    public void Add(Item item) => context.Items.Add(item);

    public void Remove(Item item) => context.Items.Remove(item);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        context.SaveChangesAsync(cancellationToken);
}
