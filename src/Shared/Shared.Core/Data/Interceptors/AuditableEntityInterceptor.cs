using Shared.Core.Web;

namespace Shared.Core.Data.Interceptors;

public class AuditableEntityInterceptor(IHttpContextService httpContextService)
    : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        UpdateEntities(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        UpdateEntities(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void UpdateEntities(DbContext? context)
    {
        if (context == null)
        {
            return;
        }

        var currentUser = GetCurrentUser();
        var currentTime = DateTime.UtcNow;

        foreach (var entry in context.ChangeTracker.Entries<IEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedBy = currentUser;
                entry.Entity.CreatedAt = currentTime;
            }

            if (entry.State == EntityState.Added || entry.State == EntityState.Modified ||
                entry.HasChangedOwnedEntities())
            {
                entry.Entity.LastModifiedBy = currentUser;
                entry.Entity.LastModified = currentTime;
            }

            // Increment version on every write — enables optimistic concurrency.
            // OriginalValue must be set to the PRE-increment value so EF's WHERE clause
            // checks the value the row had when we loaded it, not the new value.
            // Without this, a second SaveChanges in the same handler scope would generate
            // WHERE version = <old> but the DB already has <old+1>, causing a spurious 409.
            if (entry.State == EntityState.Modified)
            {
                var versionProp = entry.Property(nameof(IEntity.Version));
                versionProp.OriginalValue = entry.Entity.Version;
                entry.Entity.Version++;
            }
        }
    }

    private string GetCurrentUser()
    {
        // Try to get user ID first
        var userId = httpContextService.GetUserId();
        if (!string.IsNullOrEmpty(userId))
        {
            return userId;
        }

        // Fallback to username
        var userName = httpContextService.GetUserName();
        if (!string.IsNullOrEmpty(userName))
        {
            return userName;
        }

        // Fallback to email
        var email = httpContextService.GetEmail();
        if (!string.IsNullOrEmpty(email))
        {
            return email;
        }

        // If user is authenticated but we can't get identity
        if (httpContextService.IsAuthenticated())
        {
            return "authenticated-user";
        }

        // System/Background operations
        return "system";
    }
}

public static class Extensions
{
    public static bool HasChangedOwnedEntities(this EntityEntry entry)
    {
        return entry.References.Any(r =>
            r.TargetEntry != null &&
            r.TargetEntry.Metadata.IsOwned() &&
            (r.TargetEntry.State == EntityState.Added ||
             r.TargetEntry.State == EntityState.Modified));
    }
}
