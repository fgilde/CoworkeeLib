using Coworkee.Application.Retention;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Coworkee.Retention;

/// <summary>Claims a target with a token of its own, runs it in a scope of its own and records the result in <see cref="RetentionRun"/>.</summary>
internal sealed partial class RetentionRunner(IServiceScopeFactory scopes, CoworkeeDbContext db, TimeProvider clock, ILogger<RetentionRunner> logger)
{
    // a run that crashed with its process is given up after this
    public static readonly TimeSpan StaleAfter = TimeSpan.FromHours(2);

    private DbSet<RetentionRun> Runs => db.Set<RetentionRun>();

    public static bool IsRunning(RetentionRun? run, DateTimeOffset now) => run?.RunningSince is { } since && since > now - StaleAfter;

    /// <summary>The token of the new run, or null while another run holds the target.</summary>
    public async Task<Guid?> TryClaimAsync(string key, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var stale = now - StaleAfter;
        var token = Guid.NewGuid();
        if (await Runs.Where(r => r.Key == key && (r.RunningSince == null || r.RunningSince < stale))
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.RunningSince, now).SetProperty(r => r.RunToken, token), cancellationToken) == 1)
        {
            return token;
        }

        if (await Runs.AnyAsync(r => r.Key == key, cancellationToken))
        {
            return null;
        }

        var run = new RetentionRun { Key = key, RunningSince = now, RunToken = token };
        Runs.Add(run);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return token;
        }
        catch (DbUpdateException)
        {
            return null;
        }
        finally
        {
            db.Entry(run).State = EntityState.Detached;
        }
    }

    /// <summary>Runs a claimed target and records the result; only the run that still holds the claim releases it.</summary>
    public async Task RunAsync(string key, int days, Guid? by, Guid token, CancellationToken cancellationToken)
    {
        long? deleted = null;
        string? error = null;
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var target = scope.ServiceProvider.GetServices<IRetentionTarget>().FirstOrDefault(t => t.Key == key)
                ?? throw new InvalidOperationException($"There is no retention target '{key}'.");
            deleted = await target.DeleteAsync(clock.GetUtcNow().AddDays(-days), cancellationToken);
            LogDeleted(key, deleted.Value, days, by);
        }
        catch (RetentionIncompleteException exception)
        {
            (deleted, error) = (exception.Deleted, exception.Message);
            LogFailed(exception, key);
        }
        catch (Exception exception)
        {
            error = exception.Message;
            LogFailed(exception, key);
        }

        var now = clock.GetUtcNow();
        error = error?[..Math.Min(error.Length, 4000)];
        if (await Runs.Where(r => r.Key == key && r.RunToken == token).ExecuteUpdateAsync(s => s
                .SetProperty(r => r.RunningSince, (DateTimeOffset?)null)
                .SetProperty(r => r.RunToken, (Guid?)null)
                .SetProperty(r => r.LastRunAt, now)
                .SetProperty(r => r.LastDeleted, deleted)
                .SetProperty(r => r.LastError, error)
                .SetProperty(r => r.LastRunBy, by), CancellationToken.None) == 0)
        {
            // a later run took the claim over: its claim stays, this result is recorded
            await Runs.Where(r => r.Key == key).ExecuteUpdateAsync(s => s
                .SetProperty(r => r.LastRunAt, now)
                .SetProperty(r => r.LastDeleted, deleted)
                .SetProperty(r => r.LastError, error)
                .SetProperty(r => r.LastRunBy, by), CancellationToken.None);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Retention {Key}: deleted {Deleted} rows older than {Days} days (started by {UserId}).")]
    private partial void LogDeleted(string key, long deleted, int days, Guid? userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Retention {Key} failed.")]
    private partial void LogFailed(Exception exception, string key);
}
