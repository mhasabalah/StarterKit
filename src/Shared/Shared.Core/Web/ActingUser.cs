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
    private static readonly AsyncLocal<string?> AmbientCorrelation = new();

    /// <summary>The user the current background flow acts for, or null.</summary>
    public static Guid? Current => Ambient.Value;

    /// <summary>
    /// What the background flow is (e.g. <c>agent-run:&lt;id&gt;</c>), or null. Outside an HTTP request it is the
    /// correlation id of <see cref="IHttpContextService.GetCorrelationId"/>, so the audit trail of a change made on
    /// someone's behalf points at the work that made it.
    /// </summary>
    public static string? CorrelationId => AmbientCorrelation.Value;

    public static IDisposable Begin(Guid userId) => Begin(userId, null);

    /// <param name="userId">The user the work acts for.</param>
    /// <param name="correlationId">Optional origin of the work, recorded as the correlation id of its audit entries.</param>
    public static IDisposable Begin(Guid userId, string? correlationId)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("An acting user is required.", nameof(userId));

        var previous = Ambient.Value;
        var previousCorrelation = AmbientCorrelation.Value;
        Ambient.Value = userId;
        AmbientCorrelation.Value = correlationId ?? previousCorrelation;
        return new Restore(previous, previousCorrelation);
    }

    private sealed class Restore(Guid? previous, string? previousCorrelation) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            Ambient.Value = previous;
            AmbientCorrelation.Value = previousCorrelation;
        }
    }
}
