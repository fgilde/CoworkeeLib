using System.Data.Common;
using Coworkee.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Coworkee.Files.Attachments;

/// <summary>
/// Ties the blobs of attachments to the unit of work of the scope: written blobs stay once their rows are committed and are deleted when the scope ends without it;
/// blobs of removed rows are deleted after the commit.
/// </summary>
// ponytail: a process crash between the blob write and the commit leaves the blob without a row; a sweep of unreferenced blob keys if that ever matters
internal sealed partial class AttachmentBlobs(IBlobStorage storage, ILogger<AttachmentBlobs> logger) : SaveChangesInterceptor, IDbTransactionInterceptor, IAsyncDisposable, IDisposable
{
    private readonly List<string> _written = [];
    private readonly List<string> _removed = [];
    private readonly List<string> _savedWritten = [];
    private readonly List<string> _savedRemoved = [];
    private DbContext? _context;

    public void Written(DbContext context, string key)
    {
        _context = context;
        _written.Add(key);
    }

    public void Removed(DbContext context, string key)
    {
        _context = context;
        _removed.Add(key);
    }

    public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        await SavedAsync(eventData.Context);
        return result;
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        SavedAsync(eventData.Context).GetAwaiter().GetResult();
        return result;
    }

    public void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData) => CommitAsync().GetAwaiter().GetResult();

    public Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default) => CommitAsync();

    public void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData) => RolledBack();

    public Task TransactionRolledBackAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        RolledBack();
        return Task.CompletedTask;
    }

    // sync scopes (start-up code, older modules) dispose synchronously; without IDisposable the container refuses to dispose them at all
    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    public async ValueTask DisposeAsync()
    {
        _written.AddRange(_savedWritten);
        _savedWritten.Clear();
        await DeleteAsync(_written);
    }

    private Task SavedAsync(DbContext? context)
    {
        if (context is null || context != _context)
        {
            return Task.CompletedTask;
        }

        Move(_written, _savedWritten);
        Move(_removed, _savedRemoved);
        return context.Database.CurrentTransaction is null ? CommitAsync() : Task.CompletedTask;
    }

    private async Task CommitAsync()
    {
        _savedWritten.Clear();
        await DeleteAsync(_savedRemoved);
    }

    private void RolledBack()
    {
        Move(_savedWritten, _written);
        Move(_savedRemoved, _removed);
    }

    private static void Move(List<string> from, List<string> to)
    {
        to.AddRange(from);
        from.Clear();
    }

    // the rows decided already: a blob left behind is logged, never brings a row back
    private async Task DeleteAsync(List<string> keys)
    {
        foreach (var key in keys)
        {
            try
            {
                await storage.DeleteAsync(key, CancellationToken.None);
            }
            catch (Exception exception)
            {
                LogBlobFailed(exception, key);
            }
        }

        keys.Clear();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The blob {BlobKey} of an attachment could not be deleted.")]
    private partial void LogBlobFailed(Exception exception, string blobKey);
}
