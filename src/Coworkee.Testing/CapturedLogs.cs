using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Coworkee.Testing;

/// <summary>
/// A logger provider that keeps every record as one line: message, structured values and the scopes around it (as with IncludeScopes),
/// e.g. <c>builder.Logging.AddProvider(logs)</c> to check that nothing secret is logged.
/// </summary>
public sealed class CapturedLogs : ILoggerProvider, ISupportExternalScope
{
    private IExternalScopeProvider _scopes = new LoggerExternalScopeProvider();

    public ConcurrentQueue<string> Lines { get; } = new();

    public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;

    public void Dispose()
    {
    }

    private static string Values(object? state) => string.Join(";", (state as IEnumerable<KeyValuePair<string, object?>> ?? []).Select(v => $"{v.Key}={v.Value}"));

    private sealed class Logger(CapturedLogs logs, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => logs._scopes.Push(state);

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var scopes = new List<string>();
            logs._scopes.ForEachScope((scope, list) => list.Add($"{scope} [{Values(scope)}]"), scopes);
            logs.Lines.Enqueue($"{category}: {formatter(state, exception)} | {Values(state)} | scopes: {string.Join(" / ", scopes)}");
        }
    }
}
