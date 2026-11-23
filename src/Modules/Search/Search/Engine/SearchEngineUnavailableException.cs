namespace Search.Engine;

/// <summary>
/// The engine could not answer (down, overloaded, timed out). Searches turn it into
/// <see cref="SearchStatus.EngineUnavailable"/> so owners fall back to SQL; indexing lets it propagate, so the work is
/// retried. Programming errors are never wrapped in it.
/// </summary>
public sealed class SearchEngineUnavailableException(string engine, Exception innerException)
    : Exception($"The search engine '{engine}' is unavailable: {innerException.Message}", innerException)
{
    public string Engine { get; } = engine;
}
