using System.Globalization;
using System.Text.Json;
using Coworkee.Application.Localization;
using Coworkee.Application.Messaging;
using Coworkee.BackgroundJobs;
using Coworkee.Contracts.Data;
using Coworkee.Core.Results;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Coworkee.OData.Transfer.Import;

public sealed record ImportJobArgs(Guid JobId, bool Undo);

/// <summary>
/// Sends one command per row as the user who started the import, each in its own scope, and saves the progress now and then
/// (the owner sees it live). Never throws: a retry would import the rows twice.
/// </summary>
internal sealed class ImportJob(
    IServiceProvider services, CoworkeeDbContext db, ODataEntityRegistry registry, IEnumerable<ITextTranslator> translators, TimeProvider clock, ILogger<ImportJob> logger)
    : IBackgroundJob<ImportJobArgs>
{
    private static readonly TimeSpan ProgressEvery = TimeSpan.FromSeconds(1);
    private DateTimeOffset _saved;

    public async Task ExecuteAsync(ImportJobArgs args, CancellationToken cancellationToken)
    {
        if (await db.Set<DataTransferJob>().SingleOrDefaultAsync(j => j.Id == args.JobId, cancellationToken) is not { } job)
        {
            return;
        }

        try
        {
            if (args.Undo)
            {
                await UndoAsync(job, cancellationToken);
            }
            else if (job.Status == DataTransferStatus.Queued)
            {
                await ImportAsync(job, cancellationToken);
            }
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Import {JobId} failed.", job.Id);
            job.Status = DataTransferStatus.Failed;
            job.Message = exception.Message[..Math.Min(exception.Message.Length, 2000)];
            job.FinishedAt = clock.GetUtcNow();
            await db.SaveChangesAsync(CancellationToken.None);
        }
    }

    private async Task ImportAsync(DataTransferJob job, CancellationToken cancellationToken)
    {
        var stored = JsonSerializer.Deserialize<StoredImport>(job.Settings!, JsonSerializerOptions.Web)!;
        var ui = CultureInfo.GetCultureInfo(stored.UiCulture);
        (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = (ImportService.Culture(stored.Settings), ui);
        string Translate(string text) => translators.Translate(text, ui);

        var import = registry.FindImport(job.EntitySet) ?? throw new InvalidOperationException($"{job.EntitySet} cannot be imported anymore.");
        var table = ImportFileReader.Read(job.Input!, job.FileName!, stored.Settings.Parse);
        var planner = new ImportPlanner(services, import, stored.Settings, table.Typed, CultureInfo.CurrentCulture, Translate);
        var failures = new List<(string?[] Cells, int Row, string Error)>();
        var created = new List<Guid>();
        (job.Status, job.Total) = (DataTransferStatus.Running, table.Rows.Count);
        await SaveAsync(force: true, cancellationToken);
        for (var i = 0; i < table.Rows.Count; i++)
        {
            var plan = await planner.PlanAsync(table, i, cancellationToken);
            var error = plan.IsValid ? job.DryRun ? null : await SendAsync(plan, created, cancellationToken) : string.Join(" ", plan.Errors);
            if (error is not null)
            {
                failures.Add((table.Rows[i], plan.Row, error));
                job.Failed++;
            }
            else if (plan.Action == ImportRowPlan.Update)
            {
                job.Updated++;
            }
            else
            {
                job.Created++;
            }

            job.Processed = i + 1;
            await SaveAsync(force: false, cancellationToken);
        }

        if (failures.Count > 0)
        {
            job.Result = CsvFile.Write([[.. table.Headers, "Row", "Error"], .. failures.Select(f => f.Cells.Concat([f.Row.ToString(CultureInfo.InvariantCulture), f.Error]))]);
            job.ResultFileName = $"{Path.GetFileNameWithoutExtension(job.FileName)}-errors.csv";
            job.ResultContentType = "text/csv";
        }

        job.CreatedIds = JsonSerializer.Serialize(created);
        (job.Status, job.FinishedAt) = (DataTransferStatus.Completed, clock.GetUtcNow());
        await SaveAsync(force: true, CancellationToken.None);
    }

    private async Task<string?> SendAsync(ImportRowPlan plan, List<Guid> created, CancellationToken cancellationToken)
    {
        // every row in its own scope: a failed row must not leave tracked entities behind for the next one
        await using var scope = services.CreateAsyncScope();
        var result = await ODataImportRegistration.SendAsync(scope.ServiceProvider.GetRequiredService<IDispatcher>(), plan.Command!, cancellationToken);
        if (!result.IsSuccess)
        {
            return ODataImportRegistration.Describe(result.Error!);
        }

        if (plan.Action == ImportRowPlan.Create && ODataImportRegistration.CreatedId(result) is { } id)
        {
            created.Add(id);
        }

        return null;
    }

    private async Task UndoAsync(DataTransferJob job, CancellationToken cancellationToken)
    {
        var delete = registry.FindImport(job.EntitySet)?.Delete ?? throw new InvalidOperationException($"Imports of {job.EntitySet} cannot be undone.");
        var failed = 0;
        foreach (var id in JsonSerializer.Deserialize<List<Guid>>(job.CreatedIds ?? "[]") ?? [])
        {
            await using var scope = services.CreateAsyncScope();
            var result = await ODataImportRegistration.SendAsync(scope.ServiceProvider.GetRequiredService<IDispatcher>(), delete(id), cancellationToken);
            failed += result.IsSuccess || result.Error!.Kind == ErrorKind.NotFound ? 0 : 1;
        }

        job.Status = DataTransferStatus.Undone;
        job.Message = failed == 0 ? null : $"{failed} rows could not be deleted.";
        await SaveAsync(force: true, CancellationToken.None);
    }

    private async Task SaveAsync(bool force, CancellationToken cancellationToken)
    {
        if (force || clock.GetUtcNow() - _saved >= ProgressEvery)
        {
            _saved = clock.GetUtcNow();
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
