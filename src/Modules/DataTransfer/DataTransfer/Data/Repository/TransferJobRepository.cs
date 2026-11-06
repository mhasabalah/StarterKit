using DataTransfer.Data.Configurations;

namespace DataTransfer.Data.Repository;

/// <summary>Write-side repository for the <see cref="TransferJob"/> aggregate (command handlers and jobs).</summary>
public interface ITransferJobRepository
{
    Task<TransferJob?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task AddAsync(TransferJob job, CancellationToken cancellationToken = default);

    /// <summary>Saves; a second active import of the same definition becomes <see cref="ImportAlreadyRunningException"/>.</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>Adds row errors (saved with the next <see cref="SaveChangesAsync"/>).</summary>
    void AddErrors(IEnumerable<TransferJobError> errors);
}

public class TransferJobRepository(DataTransferDbContext context) : ITransferJobRepository
{
    public Task<TransferJob?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.Jobs.FirstOrDefaultAsync(j => j.Id == id, cancellationToken);

    public async Task AddAsync(TransferJob job, CancellationToken cancellationToken = default)
    {
        await context.Jobs.AddAsync(job, cancellationToken);
        await SaveChangesAsync(cancellationToken);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation(TransferJobConfiguration.ActiveImportIndexName))
        {
            var key = context.ChangeTracker.Entries<TransferJob>().Select(e => e.Entity.DefinitionKey).FirstOrDefault();
            throw new ImportAlreadyRunningException(key ?? string.Empty);
        }
    }

    public void AddErrors(IEnumerable<TransferJobError> errors) => context.Errors.AddRange(errors);
}
