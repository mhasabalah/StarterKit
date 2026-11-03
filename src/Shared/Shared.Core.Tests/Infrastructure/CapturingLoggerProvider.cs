using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Shared.Core.Tests.Infrastructure;

public sealed record LogEntry(
    string Category,
    LogLevel Level,
    string Message,
    Exception? Exception,
    IReadOnlyDictionary<string, object?> State,
    IReadOnlyDictionary<string, object?> Scope);

/// <summary>Captures log entries (with structured state and active scopes) for assertions.</summary>
public sealed class CapturingLoggerProvider : ILoggerProvider, ISupportExternalScope
{
    private IExternalScopeProvider _scopes = new LoggerExternalScopeProvider();

    public ConcurrentQueue<LogEntry> Entries { get; } = new();

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(this, categoryName);

    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(CapturingLoggerProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => owner._scopes.Push(state);

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var stateValues = ToDictionary(state);
            var scopeValues = new Dictionary<string, object?>();
            owner._scopes.ForEachScope((scope, acc) =>
            {
                foreach (var kv in ToDictionary(scope)) acc[kv.Key] = kv.Value;
            }, scopeValues);

            owner.Entries.Enqueue(new LogEntry(category, logLevel, formatter(state, exception), exception,
                stateValues, scopeValues));
        }

        private static Dictionary<string, object?> ToDictionary(object? state)
        {
            var result = new Dictionary<string, object?>();
            if (state is IEnumerable<KeyValuePair<string, object?>> pairs)
                foreach (var kv in pairs) result[kv.Key] = kv.Value;
            else if (state is IEnumerable<KeyValuePair<string, object>> pairs2)
                foreach (var kv in pairs2) result[kv.Key] = kv.Value;
            return result;
        }
    }
}
