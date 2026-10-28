using System.Collections.Concurrent;
using Coworkee.Domain;
using Microsoft.Extensions.Logging;

namespace Coworkee.OData.Tests;

/// <summary>A set whose rows come only when <see cref="SleeperGate.Closed"/> is off: its exports keep running until they are cancelled.</summary>
public sealed class Sleeper : AuditedEntity, IMultiTenant
{
    public required string Name { get; set; }

    public Guid TenantId { get; set; }
}

internal sealed class SleeperGate : IODataEntityFilter<Sleeper>
{
    public static volatile bool Closed;

    public async Task<IQueryable<Sleeper>> ApplyAsync(IQueryable<Sleeper> query, CancellationToken cancellationToken)
    {
        while (Closed)
        {
            await Task.Delay(50, cancellationToken);
        }

        return query;
    }
}

/// <summary>Keeps the warnings the app logs.</summary>
public sealed class RecordingLoggerProvider : ILoggerProvider
{
    public ConcurrentQueue<string> Warnings { get; } = new();

    public ILogger CreateLogger(string categoryName) => new Logger(this);

    public void Dispose()
    {
    }

    private sealed class Logger(RecordingLoggerProvider owner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
            {
                owner.Warnings.Enqueue(formatter(state, exception));
            }
        }
    }
}
