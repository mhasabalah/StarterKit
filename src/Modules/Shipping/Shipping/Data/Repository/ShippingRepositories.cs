using Shipping.Data.Configurations;
using Shipping.Domains.Declarations.Models;

namespace Shipping.Data.Repository;

/// <summary>Write-side repository of the <see cref="Shipment"/> aggregate (with its documents).</summary>
public interface IShipmentRepository
{
    Task<Shipment?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<bool> ReferenceExistsAsync(string reference, CancellationToken cancellationToken = default);

    void Add(Shipment shipment);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public class ShipmentRepository(ShippingDbContext context) : IShipmentRepository
{
    public Task<Shipment?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.Shipments.Include(s => s.Documents).FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

    public Task<bool> ReferenceExistsAsync(string reference, CancellationToken cancellationToken = default) =>
        context.Shipments.AnyAsync(s => s.Reference == reference, cancellationToken);

    public void Add(Shipment shipment) => context.Shipments.Add(shipment);

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation(ShipmentConfiguration.ReferenceIndexName))
        {
            var reference = context.ChangeTracker.Entries<Shipment>()
                .FirstOrDefault(e => e.State == EntityState.Added)?.Entity.Reference ?? string.Empty;
            throw new ShipmentAlreadyExistsException(reference); // a concurrent create won the race
        }
    }
}

/// <summary>Write-side repository of the <see cref="DeclarationDraft"/> aggregate.</summary>
public interface IDeclarationDraftRepository
{
    Task<DeclarationDraft?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<DeclarationDraft?> GetByShipmentAsync(Guid shipmentId, CancellationToken cancellationToken = default);

    void Add(DeclarationDraft draft);

    /// <summary>Saves; a second draft for the same shipment (two first prepares at once) becomes a 409.</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public class DeclarationDraftRepository(ShippingDbContext context) : IDeclarationDraftRepository
{
    public Task<DeclarationDraft?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.DeclarationDrafts.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

    public Task<DeclarationDraft?> GetByShipmentAsync(Guid shipmentId, CancellationToken cancellationToken = default) =>
        context.DeclarationDrafts.FirstOrDefaultAsync(d => d.ShipmentId == shipmentId, cancellationToken);

    public void Add(DeclarationDraft draft) => context.DeclarationDrafts.Add(draft);

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation(DeclarationDraftConfiguration.ShipmentIndexName))
        {
            throw new ConcurrencyConflictException(ex);
        }
    }
}
