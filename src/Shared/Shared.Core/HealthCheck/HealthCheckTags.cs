namespace Shared.Core.HealthCheck;

/// <summary>
/// Health check tags. <c>/alive</c> runs only <see cref="Live"/> checks (process is responsive - restart if not);
/// <c>/health</c> runs every check, including <see cref="Ready"/> dependency checks (Postgres, Redis).
/// </summary>
public static class HealthCheckTags
{
    public const string Live = "live";
    public const string Ready = "ready";
}
