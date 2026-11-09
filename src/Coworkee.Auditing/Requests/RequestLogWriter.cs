using System.Diagnostics.Metrics;
using System.Threading.Channels;
using Coworkee.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Coworkee.Auditing.Requests;

/// <summary>
/// Requests wait in a bounded queue and are saved in batches by a background loop, so logging never holds up a request. A full queue drops the request
/// and counts it (metric <c>coworkee.request_log.dropped</c> of the meter <c>Coworkee.Auditing</c>) instead of slowing the app down.
/// </summary>
internal sealed partial class RequestLogWriter(IServiceScopeFactory scopes, ILogger<RequestLogWriter> logger) : BackgroundService
{
    public const string MeterName = "Coworkee.Auditing";

    private const int Capacity = 2000;
    private const int Batch = 200;

    private static readonly Meter Meter = new(MeterName);
    private static readonly Counter<long> DroppedCounter = Meter.CreateCounter<long>("coworkee.request_log.dropped", description: "Requests left out of the request log because its queue was full.");

    private readonly Channel<RequestLogCapture> _queue = Channel.CreateBounded<RequestLogCapture>(new BoundedChannelOptions(Capacity) { SingleReader = true });
    private long _dropped;

    public long Dropped => Interlocked.Read(ref _dropped);

    public void Write(RequestLogCapture capture)
    {
        if (_queue.Writer.TryWrite(capture))
        {
            return;
        }

        DroppedCounter.Add(1);
        if (Interlocked.Increment(ref _dropped) % 1000 == 1)
        {
            LogDropped(Dropped);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var batch = new List<RequestLogCapture>(Batch);
        try
        {
            while (await _queue.Reader.WaitToReadAsync(stoppingToken))
            {
                await DrainAsync(batch, CancellationToken.None);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // stopping: what is queued still goes in
            await DrainAsync(batch, CancellationToken.None);
        }
    }

    private async Task DrainAsync(List<RequestLogCapture> batch, CancellationToken cancellationToken)
    {
        while (_queue.Reader.TryPeek(out _))
        {
            while (batch.Count < Batch && _queue.Reader.TryRead(out var capture))
            {
                batch.Add(capture);
            }

            await SaveAsync(batch, cancellationToken);
            batch.Clear();
        }
    }

    private async Task SaveAsync(List<RequestLogCapture> batch, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<CoworkeeDbContext>();
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            db.AddRange(batch.Select(c => c.ToEntry()));
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogFailed(exception, batch.Count);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The request log queue is full: {Dropped} requests were not logged since the start")]
    private partial void LogDropped(long dropped);

    [LoggerMessage(Level = LogLevel.Error, Message = "{Count} requests could not be saved to the request log")]
    private partial void LogFailed(Exception exception, int count);
}
