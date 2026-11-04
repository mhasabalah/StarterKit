namespace Shared.Core.Web;

/// <summary>
/// Named request-timeout policies (<c>AddRequestTimeouts</c> in the host). The default policy is
/// <c>RequestTimeouts:DefaultSeconds</c>; endpoints that stream large bodies (file upload/download) opt in to
/// <see cref="LongRunning"/> (<c>RequestTimeouts:LongRunningSeconds</c>) with <c>.WithRequestTimeout(...)</c>.
/// </summary>
public static class RequestTimeoutPolicies
{
    public const string LongRunning = "long-running";
}
