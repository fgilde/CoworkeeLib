using Coworkee.Infrastructure.Persistence;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Coworkee.Infrastructure.Outbox;

internal sealed partial class OutboxBackgroundService<TContext>(OutboxProcessor<TContext> processor, ILogger<OutboxBackgroundService<TContext>> logger)
    : BackgroundService
    where TContext : CoworkeeDbContext
{
    private const int BatchSize = 50;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // ponytail: 1 s polling, add commit signal (LISTEN/NOTIFY) when event latency matters
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        do
        {
            try
            {
                while (await processor.ProcessBatchAsync(BatchSize, stoppingToken) == BatchSize)
                {
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogBatchFailed(exception);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Outbox batch failed, retrying on next tick")]
    private partial void LogBatchFailed(Exception exception);
}
