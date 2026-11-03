using Shared.Core.Web;

namespace Shared.Core.Data.Interceptors;

/// <summary>
/// Stamps audit fields and bumps the optimistic-concurrency <c>Version</c> on save.
/// Time comes from <see cref="TimeProvider"/> (registered as <see cref="TimeProvider.System"/>; tests may inject a fake).
/// </summary>
public class AuditableEntityInterceptor(IHttpContextService httpContextService, TimeProvider? timeProvider = null)
    : SaveChangesInterceptor
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

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
        var currentTime = _timeProvider.GetUtcNow().UtcDateTime;

        foreach (var entry in context.ChangeTracker.Entries<IEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedBy = currentUser;
                entry.Entity.CreatedAt = currentTime;
                entry.Entity.LastModifiedBy = currentUser;
                entry.Entity.LastModified = currentTime;
                continue; // New rows keep Version = 0; there is nothing to check on INSERT.
            }

            var ownedChanged = entry.HasChangedOwnedEntities();
            if (entry.State != EntityState.Modified && !ownedChanged)
            {
                continue;
            }

            entry.Entity.LastModifiedBy = currentUser;
            entry.Entity.LastModified = currentTime;

            BumpVersion(entry);
        }
    }

    /// <summary>
    /// Optimistic concurrency. EF emits <c>UPDATE ... WHERE version = @OriginalValue</c>, so:
    /// <list type="bullet">
    /// <item><c>OriginalValue</c> = the version the caller expects the row to have. It is the loaded value by
    /// default, or the client-supplied value when a handler called
    /// <see cref="DbContextVersioningExtensions.ExpectVersion{TEntity}"/>. This interceptor must NEVER
    /// overwrite it. The previous implementation reset it to the loaded value, which silently turned every
    /// stale-version request into a successful "last write wins" update.</item>
    /// <item><c>CurrentValue</c> = <c>OriginalValue + 1</c>. This is also correct for a second SaveChanges in the
    /// same scope, because EF accepts changes after each save (original == current again).</item>
    /// <item>A change to an owned entity only (the owner is still <c>Unchanged</c>) bumps the version too. That marks
    /// the owner <c>Modified</c>, so the aggregate row is updated and concurrency-checked.</item>
    /// </list>
    /// </summary>
    private static void BumpVersion(EntityEntry<IEntity> entry)
    {
        var versionProp = entry.Property(e => e.Version);
        versionProp.CurrentValue = versionProp.OriginalValue + 1;
        versionProp.IsModified = true;
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
