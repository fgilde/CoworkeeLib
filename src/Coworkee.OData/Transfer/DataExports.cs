using System.Net.Mail;
using System.Reflection;
using System.Text.Json;
using System.Threading.Channels;
using Coworkee.Application;
using Coworkee.Application.Authorization;
using Coworkee.Application.Groups;
using Coworkee.BackgroundJobs;
using Coworkee.Contracts.Data;
using Coworkee.Core.Security;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Infrastructure.Tokens;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Coworkee.OData.Transfer;

/// <param name="Groups">The groups selected when the export was requested; the job lists the rows of them, as the user's list did.</param>
/// <param name="Mail">Mails the file to the owner and <paramref name="Recipients"/> when it is written.</param>
public sealed record ExportJobArgs(Guid JobId, Guid[]? Groups = null, bool Mail = false, string[]? Recipients = null);

/// <summary>Writes an export too large for the request; the owner gets the file through the job (the client shows a download link).</summary>
public sealed partial class DataExports(
    IServiceProvider services, CoworkeeDbContext db, ODataEntityRegistry registry, ICurrentUser user, TimeProvider clock, IOptions<CoworkeeODataOptions> options, ILogger<DataExports> logger)
{
    public const string DownloadPurpose = "data-export";

    public const int MaxRecipients = 20;

    /// <summary>The one-time download link of a streamed export works this long.</summary>
    public static readonly TimeSpan DownloadLifetime = TimeSpan.FromMinutes(5);

    private static readonly MethodInfo WriteMethod = typeof(DataExport).GetMethod(nameof(DataExport.WriteAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

    /// <summary>The file, or the job that writes it when the rows are more than <see cref="CoworkeeODataOptions.BackgroundExportRows"/>.</summary>
    public async Task<IResult> ExportAsync(string entitySet, QueryString query, CancellationToken cancellationToken)
    {
        if (await CheckAsync(entitySet, cancellationToken) is { } denied)
        {
            return denied;
        }

        var entity = registry.Find(entitySet)!;
        ExportFile? file;
        try
        {
            file = await WriteAsync(entity, query, options.Value.BackgroundExportRows, cancellationToken);
        }
        catch (UnauthorizedAccessException)
        {
            return Results.Forbid();
        }
        catch (Microsoft.OData.ODataException exception)
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "The query is not valid.", detail: exception.Message);
        }

        return file is not null ? Results.File(file.Content, file.ContentType, file.Name) : await EnqueueAsync(entity, query, null, null, cancellationToken);
    }

    /// <summary>
    /// The export as a stream of progress lines (application/x-ndjson) ending with a ready line and its one-time download link. The export goes on when the
    /// client leaves; its file is then mailed to the user. Either way the job keeps the file.
    /// </summary>
    public async Task<IResult> StreamAsync(string entitySet, HttpContext http)
    {
        if (await CheckAsync(entitySet, http.RequestAborted) is { } denied)
        {
            return denied;
        }

        var entity = registry.Find(entitySet)!;
        var query = http.Request.QueryString;
        if (await ReserveAsync(entity, query, null, DataTransferStatus.Running, http.RequestAborted) is not { } job)
        {
            return TooManyExports();
        }

        http.Response.ContentType = "application/x-ndjson";
        var lines = Channel.CreateUnbounded<DataExportProgress>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
        // the first line names the job at once, so a client can cancel it before the export reports anything
        lines.Writer.TryWrite(new DataExportProgress(DataExportProgress.Counting, 0, JobId: job.Id));
        var writing = WriteLinesAsync(lines.Reader, http);
        ExportFile? file = null;
        var queued = false;
        // not the request's token: the export finishes when the client leaves, but not later than the timeout or a cancel
        using var timeout = new CancellationTokenSource(options.Value.ExportTimeout);
        using var watch = services.GetRequiredService<ExportCancellations>().Start(job.Id, timeout.Token);
        try
        {
            file = await WriteAsync(entity, query, options.Value.BackgroundExportRows, watch.Token, new LineProgress(lines.Writer, job.Id));
            if (file is null)
            {
                // too many rows for a request: the export job writes it; the client follows the job like any background export
                (job.Status, job.StartedAt, queued) = (DataTransferStatus.Queued, null, true);
            }
            else
            {
                (job.Result, job.ResultContentType, job.ResultFileName, job.Status) = (file.Content, file.ContentType, job.FileName ?? file.Name, DataTransferStatus.Completed);
            }
        }
        catch (OperationCanceledException) when (watch.Cancelled)
        {
        }
        catch (Exception exception)
        {
            LogFailed(exception, job.Id);
            (job.Status, job.Message) = (DataTransferStatus.Failed, exception switch
            {
                UnauthorizedAccessException => exception.Message,
                Microsoft.OData.ODataException => $"The query is not valid: {exception.Message}",
                OperationCanceledException => "The export took too long.",
                _ => "Export failed",
            });
        }

        lines.Writer.Complete();
        var connected = await writing;
        if (watch.Cancelled)
        {
            // the cancel saved the job already: no file, no mail, and the slot is free
            await WriteLineAsync(http, new DataExportProgress(DataExportProgress.Cancelled, 100, JobId: job.Id));
            return Results.Empty;
        }

        if (queued)
        {
            await db.SaveChangesAsync(CancellationToken.None);
            var groups = services.GetService<IGroupScope>() is { } scope ? await scope.SelectedAsync(CancellationToken.None) : [];
            services.GetRequiredService<IBackgroundJobs>().Enqueue<ExportJob, ExportJobArgs>(new ExportJobArgs(job.Id, [.. groups], Mail: !connected));
            await WriteLineAsync(http, new DataExportProgress(DataExportProgress.Queued, 100, JobId: job.Id));
            return Results.Empty;
        }

        job.FinishedAt = clock.GetUtcNow();
        var token = file is not null && connected ? services.GetRequiredService<IAccessTokens>().Issue(DownloadPurpose, job.Id, DownloadLifetime, singleUse: true) : null;
        try
        {
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (DbUpdateConcurrencyException)
        {
            // the stale sweep failed the job meanwhile, or the user cancelled it as it ended
            var cancelled = watch.Cancelled || await db.Set<DataTransferJob>().AsNoTracking().AnyAsync(j => j.Id == job.Id && j.Status == DataTransferStatus.Cancelled, CancellationToken.None);
            await WriteLineAsync(http, cancelled
                ? new DataExportProgress(DataExportProgress.Cancelled, 100, JobId: job.Id)
                : new DataExportProgress(DataExportProgress.Failed, 100, JobId: job.Id, Message: StaleExportJob.Message));
            return Results.Empty;
        }

        if (file is null)
        {
            await WriteLineAsync(http, new DataExportProgress(DataExportProgress.Failed, 100, JobId: job.Id, Message: job.Message));
        }
        else if (token is null || !await WriteLineAsync(http, new DataExportProgress(DataExportProgress.Ready, 100, $"api/v1/data/downloads/{token}", job.ResultFileName, job.Id)))
        {
            await MailAsync(new DataExportMail(job.UserId, [], job.EntitySet, file with { Name = job.ResultFileName! }), CancellationToken.None);
            await db.SaveChangesAsync(CancellationToken.None);
        }

        return Results.Empty;
    }

    /// <summary>
    /// Cancels a running or queued export of the current user: the job becomes <see cref="DataTransferStatus.Cancelled"/>, which frees its slot at once, and the
    /// export stops without a file or mail (on this instance at once, on others when they read the row). A finished job stays as it is. Other users get 404.
    /// </summary>
    public async Task<Coworkee.Core.Results.Result<DataTransferJobDto>> CancelAsync(Guid id, CancellationToken cancellationToken)
    {
        var job = await db.Set<DataTransferJob>().SingleOrDefaultAsync(j => j.Id == id && j.UserId == user.UserId && j.Kind == DataTransferKind.Export, cancellationToken);
        if (job is { Status: DataTransferStatus.Running or DataTransferStatus.Queued })
        {
            (job.Status, job.Message, job.FinishedAt) = (DataTransferStatus.Cancelled, "Cancelled", clock.GetUtcNow());
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                services.GetRequiredService<ExportCancellations>().Trip(id);
            }
            catch (DbUpdateConcurrencyException)
            {
                // it ended meanwhile; the answer shows how
                db.ChangeTracker.Clear();
            }
        }

        return await services.GetRequiredService<DataTransferJobs>().GetAsync(id, cancellationToken);
    }

    /// <summary>Writes the export in the background and mails it to the user and <see cref="MailExportRequest.Recipients"/>.</summary>
    public async Task<IResult> MailAsync(string entitySet, MailExportRequest request, CancellationToken cancellationToken)
    {
        var recipients = request.Recipients?.Where(r => !string.IsNullOrWhiteSpace(r)).Select(r => r.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() ?? [];
        if (InvalidRecipients(recipients) is { } invalid)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["Recipients"] = [invalid] });
        }

        if (recipients.Length > 0 && !await services.GetRequiredService<IPermissionChecker>().IsGrantedAsync(DataTransferPermissions.ExportSchedules, cancellationToken))
        {
            return Results.Forbid();
        }

        if (await CheckAsync(entitySet, cancellationToken) is { } denied)
        {
            return denied;
        }

        return await EnqueueAsync(registry.Find(entitySet)!, Query(request.Query), request.FileName, recipients, cancellationToken);
    }

    /// <summary>The file behind a ready line's link, once, within <see cref="DownloadLifetime"/>; no sign-in.</summary>
    public async Task<IResult> DownloadAsync(string token, CancellationToken cancellationToken)
    {
        if (await services.GetRequiredService<IAccessTokens>().RedeemAsync(DownloadPurpose, token, cancellationToken) is not { } grant)
        {
            return Results.NotFound();
        }

        var file = await db.Set<DataTransferJob>().IgnoreQueryFilters([CoworkeeDbContext.TenantFilter]).AsNoTracking()
            .Where(j => j.Id == grant.TargetId && j.TenantId == (grant.TenantId ?? Guid.Empty) && j.UserId == grant.UserId && j.Result != null)
            .Select(j => new { j.Result, j.ResultContentType, j.ResultFileName })
            .SingleOrDefaultAsync(cancellationToken);
        return file is null ? Results.NotFound() : Results.File(file.Result!, file.ResultContentType ?? "application/octet-stream", file.ResultFileName ?? "export");
    }

    /// <summary>Why <paramref name="recipients"/> cannot be mailed (too many, no e-mail address); null when they are fine.</summary>
    public static string? InvalidRecipients(IReadOnlyCollection<string> recipients) =>
        recipients.Count > MaxRecipients ? $"At most {MaxRecipients} recipients."
        : recipients.FirstOrDefault(r => r.Length > 254 || !MailAddress.TryCreate(r, out var address) || address.Address != r) is { } wrong ? $"{wrong} is no e-mail address."
        : null;

    public const string NoGroupsLeft = "None of the selected groups is accessible anymore.";

    /// <summary>
    /// The saved group selection still accessible to the current user; null when nothing was selected (all groups). Empty when groups were selected but none
    /// is left: the caller fails rather than widen the export to every group.
    /// </summary>
    internal static async Task<IReadOnlyList<Guid>?> GroupsAsync(IServiceProvider services, IReadOnlyCollection<Guid> saved, CancellationToken cancellationToken) =>
        saved.Count == 0 || services.GetService<IGroupScope>() is not { } scope
            ? null
            : [.. saved.Intersect(await scope.AccessibleAsync(cancellationToken))];

    /// <summary>An export's query string with or without its question mark.</summary>
    public static QueryString Query(string? query) => string.IsNullOrEmpty(query) ? QueryString.Empty : new QueryString(query[0] == '?' ? query : "?" + query);

    internal Task<ExportFile?> WriteAsync(
        ODataEntityRegistration entity, QueryString query, int? maxRows, CancellationToken cancellationToken, IProgress<DataExportProgress>? progress = null) =>
        (Task<ExportFile?>)WriteMethod.MakeGenericMethod(entity.EntityType).Invoke(null, [services, entity.EntitySet, query, maxRows, progress, cancellationToken])!;

    internal async Task MailAsync(DataExportMail mail, CancellationToken cancellationToken)
    {
        if (services.GetService<IExportMailer>() is { } mailer)
        {
            await mailer.MailAsync(mail, cancellationToken);
        }
    }

    internal DataTransferJob NewJob(ODataEntityRegistration entity, QueryString query, string? fileName, DataTransferStatus status) => new()
    {
        TenantId = user.TenantId ?? Guid.Empty,
        UserId = user.UserId!.Value,
        Kind = DataTransferKind.Export,
        EntitySet = entity.EntitySet,
        Status = status,
        Settings = query.Value,
        FileName = FileName(fileName, entity.EntitySet, query),
        CreatedAt = clock.GetUtcNow(),
    };

    /// <summary>The file name with the extension of the format; without a name the set's.</summary>
    internal static string FileName(string? name, string entitySet, QueryString query)
    {
        var extension = "." + DataExport.Format(FormatOf(query)).ToString().ToLowerInvariant();
        var clean = string.Concat((name ?? string.Empty).Where(c => !Path.GetInvalidFileNameChars().Contains(c))).Trim();
        clean = clean.Length == 0 ? entitySet : clean[..Math.Min(clean.Length, 200)];
        return clean.EndsWith(extension, StringComparison.OrdinalIgnoreCase) ? clean : clean + extension;
    }

    /// <summary>
    /// Whether the user has <see cref="CoworkeeODataOptions.MaxConcurrentExports"/> exports running or queued. Ones older than
    /// <see cref="CoworkeeODataOptions.ExportTimeout"/> are stale (a server stopped while writing them) and fail first.
    /// </summary>
    /// <summary>
    /// The job of a new export, saved; null when the user has <see cref="CoworkeeODataOptions.MaxConcurrentExports"/> open. Check and insert run under a lock per
    /// user, so parallel requests cannot all pass the check.
    /// </summary>
    private async Task<DataTransferJob?> ReserveAsync(
        ODataEntityRegistration entity, QueryString query, string? fileName, DataTransferStatus status, CancellationToken cancellationToken)
    {
        await using var held = await services.GetRequiredService<IDistributedLock>().AcquireAsync($"coworkee-exports:{user.UserId}", TimeSpan.FromSeconds(10), cancellationToken);
        if (held is null || await BusyAsync(cancellationToken))
        {
            return null;
        }

        var job = NewJob(entity, query, fileName, status);
        job.StartedAt = status == DataTransferStatus.Running ? job.CreatedAt : null;
        db.Add(job);
        await db.SaveChangesAsync(cancellationToken);
        return job;
    }

    private static IResult TooManyExports() =>
        Results.Problem(title: "You have too many exports running. Wait until one is ready.", statusCode: StatusCodes.Status429TooManyRequests,
            extensions: new Dictionary<string, object?> { ["code"] = "data_transfer.too_many_exports" });

    private async Task<bool> BusyAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var mine = db.Set<DataTransferJob>().Where(j => j.UserId == user.UserId);
        foreach (var job in await mine.Where(StaleExportJob.Stale(now, options.Value.ExportTimeout)).ToListAsync(cancellationToken))
        {
            StaleExportJob.Fail(job, now);
        }

        await StaleExportJob.SaveAsync(db, cancellationToken);
        return await mine.CountAsync(j => j.Kind == DataTransferKind.Export && (j.Status == DataTransferStatus.Running || j.Status == DataTransferStatus.Queued), cancellationToken)
            >= options.Value.MaxConcurrentExports;
    }

    private async Task<IResult?> CheckAsync(string entitySet, CancellationToken cancellationToken) =>
        registry.Find(entitySet) is not { } entity ? Results.NotFound()
        : !await ODataEntityAccess.IsGrantedAsync(services, entity.EntityType, cancellationToken) ? Results.Forbid()
        : null;

    private async Task<IResult> EnqueueAsync(ODataEntityRegistration entity, QueryString query, string? fileName, string[]? recipients, CancellationToken cancellationToken)
    {
        if (await ReserveAsync(entity, query, fileName, DataTransferStatus.Queued, cancellationToken) is not { } job)
        {
            return TooManyExports();
        }

        var groups = services.GetService<IGroupScope>() is { } scope ? await scope.SelectedAsync(cancellationToken) : [];
        services.GetRequiredService<IBackgroundJobs>().Enqueue<ExportJob, ExportJobArgs>(new ExportJobArgs(job.Id, [.. groups], recipients is not null, recipients));
        var dto = await services.GetRequiredService<DataTransferJobs>().GetAsync(job.Id, cancellationToken);
        return Results.Accepted($"/api/v1/data/jobs/{job.Id}", dto.Value);
    }

    /// <summary>Sends the progress lines while the export runs; false once the client is gone.</summary>
    private static async Task<bool> WriteLinesAsync(ChannelReader<DataExportProgress> lines, HttpContext http)
    {
        var connected = true;
        await foreach (var line in lines.ReadAllAsync())
        {
            connected = connected && await WriteLineAsync(http, line);
        }

        return connected && !http.RequestAborted.IsCancellationRequested;
    }

    private static async Task<bool> WriteLineAsync(HttpContext http, DataExportProgress line)
    {
        if (http.RequestAborted.IsCancellationRequested)
        {
            return false;
        }

        try
        {
            await http.Response.WriteAsync(JsonSerializer.Serialize(line, JsonSerializerOptions.Web) + "\n", http.RequestAborted);
            await http.Response.Body.FlushAsync(http.RequestAborted);
            return true;
        }
        catch (Exception exception) when (exception is OperationCanceledException or IOException or ObjectDisposedException)
        {
            return false;
        }
    }

    private static string? FormatOf(QueryString query) =>
        Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(query.Value).TryGetValue("format", out var format) ? format.ToString() : null;

    [LoggerMessage(Level = LogLevel.Error, Message = "Export {JobId} failed.")]
    private partial void LogFailed(Exception exception, Guid jobId);

    // in order and at once: Progress<T> would post the lines to the thread pool
    private sealed class LineProgress(ChannelWriter<DataExportProgress> lines, Guid jobId) : IProgress<DataExportProgress>
    {
        public void Report(DataExportProgress value) => lines.TryWrite(value with { JobId = jobId });
    }
}
