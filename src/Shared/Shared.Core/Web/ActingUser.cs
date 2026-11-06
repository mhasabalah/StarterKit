namespace Shared.Core.Web;

/// <summary>
/// "This background work runs on behalf of user X." There is no HTTP request in a Hangfire job, so
/// <see cref="IHttpContextService.GetUserId"/> would return null and the work would look like the system. Inside a
/// <see cref="Begin"/> block, and ONLY when there is no HTTP context, <see cref="HttpContextService"/> answers with this
/// user. That gives audit columns (<c>CreatedBy</c>), the audit trail and the single-tenant subscriber (plan quotas)
/// the real actor.
/// <para>
/// Grants nothing: no roles, no permissions, no claims. The work must have authorized the user itself (e.g. the
/// DataTransfer runner re-checks the requester's membership before it begins the scope). Keep the block small and
/// explicit, like <c>TenantScope</c>.
/// </para>
/// </summary>
public static class ActingUser
{
    private static readonly AsyncLocal<Guid?> Ambient = new();

    /// <summary>The user the current background flow acts for, or null.</summary>
    public static Guid? Current => Ambient.Value;

    public static IDisposable Begin(Guid userId)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("An acting user is required.", nameof(userId));

        var previous = Ambient.Value;
        Ambient.Value = userId;
        return new Restore(previous);
    }

    private sealed class Restore(Guid? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            Ambient.Value = previous;
        }
    }
}
