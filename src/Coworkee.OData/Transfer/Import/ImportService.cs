using System.Globalization;
using System.Text.Json;
using Coworkee.Application.Localization;
using Coworkee.BackgroundJobs;
using Coworkee.Contracts.Data;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.OData.Transfer.Import;

/// <summary>The steps of the import wizard: upload, read again with other options, preview, start, undo.</summary>
public sealed class ImportService(
    IServiceProvider services, CoworkeeDbContext db, ODataEntityRegistry registry, ICurrentUser user, IEnumerable<ITextTranslator> translators, TimeProvider clock)
{
    public const int MaxFileBytes = 20 * 1024 * 1024;
    private const int PreviewRows = 200;
    private const int ShownRows = 50;

    public async Task<IReadOnlyList<ImportTargetDto>> TargetsAsync(CancellationToken cancellationToken)
    {
        var targets = new List<ImportTargetDto>();
        foreach (var import in registry.Imports)
        {
            if (await GrantedAsync(import, cancellationToken))
            {
                targets.Add(Target(import));
            }
        }

        return targets;
    }

    public async Task<Result<ImportTargetDto>> TargetAsync(string entitySet, CancellationToken cancellationToken)
    {
        var import = await FindAsync(entitySet, cancellationToken);
        return import.IsSuccess ? Target(import.Value) : import.Error!;
    }

    public async Task<Result<ImportSheetDto>> UploadAsync(string entitySet, string fileName, byte[] content, CancellationToken cancellationToken)
    {
        if (await FindAsync(entitySet, cancellationToken) is { IsSuccess: false } missing)
        {
            return missing.Error!;
        }

        if (!ImportFileReader.Extensions.Contains(Path.GetExtension(fileName).ToLowerInvariant()) || content.Length > MaxFileBytes)
        {
            return Error.Validation("file", Translate("Upload a CSV, Excel (.xlsx) or JSON file of at most 20 MB."));
        }

        var job = new DataTransferJob
        {
            TenantId = user.TenantId ?? Guid.Empty,
            UserId = user.UserId!.Value,
            Kind = DataTransferKind.Import,
            EntitySet = registry.FindImport(entitySet)!.EntitySet,
            Status = DataTransferStatus.Draft,
            FileName = Path.GetFileName(fileName),
            Input = content,
            CreatedAt = clock.GetUtcNow(),
        };
        db.Add(job);
        var sheet = Sheet(job, null);
        if (sheet.IsSuccess)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return sheet;
    }

    public async Task<Result<ImportSheetDto>> SheetAsync(Guid jobId, ImportParseOptions options, CancellationToken cancellationToken)
    {
        var draft = await DraftAsync(jobId, cancellationToken);
        return draft.IsSuccess ? Sheet(draft.Value, options) : draft.Error!;
    }

    public async Task<Result<ImportPreviewDto>> PreviewAsync(Guid jobId, ImportSettings settings, CancellationToken cancellationToken)
    {
        var ready = await PlannerAsync(jobId, settings, cancellationToken);
        if (!ready.IsSuccess)
        {
            return ready.Error!;
        }

        var (table, planner) = ready.Value;
        var plans = new List<ImportRowPlan>();
        for (var i = 0; i < table.Rows.Count && i < PreviewRows; i++)
        {
            plans.Add(await planner.PlanAsync(table, i, cancellationToken));
        }

        return new ImportPreviewDto(
            table.Rows.Count,
            plans.Count,
            plans.Count(p => p.IsValid),
            plans.Count(p => p.IsValid && p.Action == ImportRowPlan.Create),
            plans.Count(p => p.IsValid && p.Action == ImportRowPlan.Update),
            [.. plans.Take(ShownRows).Select(Dto)],
            [.. plans.Where(p => !p.IsValid).Take(ShownRows).Select(Dto)]);
    }

    public async Task<Result<DataTransferJobDto>> StartAsync(Guid jobId, ImportSettings settings, CancellationToken cancellationToken)
    {
        var ready = await PlannerAsync(jobId, settings, cancellationToken);
        if (!ready.IsSuccess)
        {
            return ready.Error!;
        }

        var job = await db.Set<DataTransferJob>().SingleAsync(j => j.Id == jobId, cancellationToken);
        if (settings.DryRun)
        {
            // a dry run checks a copy, so the upload can be imported afterwards
            job = new DataTransferJob
            {
                TenantId = job.TenantId, UserId = job.UserId, Kind = job.Kind, EntitySet = job.EntitySet, FileName = job.FileName, Input = job.Input, CreatedAt = clock.GetUtcNow(),
            };
            db.Add(job);
        }

        job.Settings = JsonSerializer.Serialize(new StoredImport(settings with { Culture = Culture(settings).Name }, CultureInfo.CurrentUICulture.Name), JsonSerializerOptions.Web);
        job.DryRun = settings.DryRun;
        job.Total = ready.Value.Table.Rows.Count;
        job.Status = DataTransferStatus.Queued;
        await db.SaveChangesAsync(cancellationToken);
        services.GetRequiredService<IBackgroundJobs>().Enqueue<ImportJob, ImportJobArgs>(new ImportJobArgs(job.Id, Undo: false));
        return await services.GetRequiredService<DataTransferJobs>().GetAsync(job.Id, cancellationToken);
    }

    public async Task<Result<DataTransferJobDto>> UndoAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var jobs = services.GetRequiredService<DataTransferJobs>();
        if (await jobs.GetAsync(jobId, cancellationToken) is not { IsSuccess: true } job)
        {
            return Error.NotFound("import.not_found", "There is no such import.");
        }

        if (!job.Value.CanUndo)
        {
            return Error.Conflict("import.undo", Translate("Only the last import that created rows can be undone."));
        }

        var stored = await db.Set<DataTransferJob>().SingleAsync(j => j.Id == jobId, cancellationToken);
        stored.Status = DataTransferStatus.Running;
        stored.Message = "Undoing";
        await db.SaveChangesAsync(cancellationToken);
        services.GetRequiredService<IBackgroundJobs>().Enqueue<ImportJob, ImportJobArgs>(new ImportJobArgs(jobId, Undo: true));
        return await jobs.GetAsync(jobId, cancellationToken);
    }

    /// <summary>The one-step import: columns named like fields, new rows only, run in the request.</summary>
    public async Task<Result<ImportResult>> ImportNowAsync(string entitySet, string fileName, byte[] content, CancellationToken cancellationToken)
    {
        var import = await FindAsync(entitySet, cancellationToken);
        if (!import.IsSuccess)
        {
            return import.Error!;
        }

        var table = ImportFileReader.Read(content, fileName, new ImportParseOptions(HasHeader: true));
        var fields = ImportField.Of(import.Value, registry);
        var settings = new ImportSettings([.. table.Headers.Select(h => fields.FirstOrDefault(f => !f.KeyOnly && string.Equals(f.Name, h, StringComparison.OrdinalIgnoreCase))?.Name)]);
        var planner = new ImportPlanner(services, import.Value, settings, table.Typed, CultureInfo.CurrentCulture, Translate);
        var errors = new List<ImportRowError>();
        for (var i = 0; i < table.Rows.Count; i++)
        {
            var plan = await planner.PlanAsync(table, i, cancellationToken);
            if (!plan.IsValid)
            {
                errors.Add(new ImportRowError(plan.Row, string.Join(" ", plan.Errors)));
                continue;
            }

            await using var scope = services.CreateAsyncScope();
            var result = await ODataImportRegistration.SendAsync(scope.ServiceProvider.GetRequiredService<Application.Messaging.IDispatcher>(), plan.Command!, cancellationToken);
            if (!result.IsSuccess)
            {
                errors.Add(new ImportRowError(plan.Row, ODataImportRegistration.Describe(result.Error!)));
            }
        }

        return new ImportResult(table.Rows.Count - errors.Count, errors);
    }

    internal async Task<Result<ODataImportRegistration>> FindAsync(string entitySet, CancellationToken cancellationToken) =>
        registry.FindImport(entitySet) is not { } import ? Error.NotFound("import.unknown_set", $"Rows of '{entitySet}' cannot be imported.")
        : !await GrantedAsync(import, cancellationToken) ? Error.Forbidden("import.forbidden", $"You may not import {import.EntitySet}.")
        : import;

    internal static CultureInfo Culture(ImportSettings settings) =>
        string.IsNullOrWhiteSpace(settings.Culture) ? CultureInfo.CurrentCulture : CultureInfo.GetCultureInfo(settings.Culture);

    private async Task<bool> GrantedAsync(ODataImportRegistration import, CancellationToken cancellationToken) =>
        registry.Find(import.EntitySet) is { } entity && await ODataEntityAccess.IsGrantedAsync(services, entity.EntityType, cancellationToken);

    private ImportTargetDto Target(ODataImportRegistration import)
    {
        var fields = ImportField.Of(import, registry);
        var entity = registry.Find(import.EntitySet)!.EntityType;
        return new ImportTargetDto(
            import.EntitySet,
            [.. fields.Select(f => f.ToDto())],
            import.Update is not null,
            import.Delete is not null,
            import.Update is null ? [] : [.. fields.Where(f => entity.GetProperty(f.Name) is { } p && ImportField.IsSimple(p.PropertyType)).Select(f => f.Name)]);
    }

    private Result<ImportSheetDto> Sheet(DataTransferJob job, ImportParseOptions? options)
    {
        ImportTable table;
        try
        {
            table = ImportFileReader.Read(job.Input!, job.FileName!, options);
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidDataException or IOException or NotSupportedException)
        {
            return Error.Validation("file", Translate("The file cannot be read: {0}", exception.Message));
        }

        var suggested = ImportColumnMatcher.Suggest(table.Headers, ImportField.Of(registry.FindImport(job.EntitySet)!, registry), Translate);
        return new ImportSheetDto(
            job.Id,
            job.FileName!,
            table.Rows.Count,
            table.Delimiter,
            table.Encoding,
            table.HasHeader,
            [.. table.Headers.Select((header, i) => new ImportColumnDto(i, header, [.. table.Rows.Take(5).Select(r => i < r.Length ? r[i] : null)], suggested[i]))]);
    }

    private async Task<Result<(ImportTable Table, ImportPlanner Planner)>> PlannerAsync(Guid jobId, ImportSettings settings, CancellationToken cancellationToken)
    {
        if (await DraftAsync(jobId, cancellationToken) is not { IsSuccess: true } draft)
        {
            return Error.NotFound("import.not_found", "There is no such import.");
        }

        var import = await FindAsync(draft.Value.EntitySet, cancellationToken);
        if (!import.IsSuccess)
        {
            return import.Error!;
        }

        if (!string.IsNullOrWhiteSpace(settings.Culture) && !CultureInfo.GetCultures(CultureTypes.AllCultures).Any(c => c.Name == settings.Culture))
        {
            return Error.Validation(nameof(settings.Culture), Translate("Unknown language."));
        }

        var table = ImportFileReader.Read(draft.Value.Input!, draft.Value.FileName!, settings.Parse);
        var planner = new ImportPlanner(services, import.Value, settings, table.Typed, Culture(settings), Translate);
        return planner.Check() is { } problem ? Error.Validation(nameof(settings.Columns), Translate(problem)) : (table, planner);
    }

    private async Task<Result<DataTransferJob>> DraftAsync(Guid jobId, CancellationToken cancellationToken) =>
        await db.Set<DataTransferJob>().AsNoTracking()
            .SingleOrDefaultAsync(j => j.Id == jobId && j.UserId == user.UserId && j.Kind == DataTransferKind.Import && j.Status == DataTransferStatus.Draft, cancellationToken) is { } job
            ? job
            : Error.NotFound("import.not_found", "There is no such import.");

    private static ImportPreviewRowDto Dto(ImportRowPlan plan) => new(plan.Row, plan.Action, plan.Values, plan.Errors);

    private string Translate(string text) => translators.Translate(text, CultureInfo.CurrentUICulture);

    private string Translate(string text, params object?[] arguments) => string.Format(CultureInfo.CurrentCulture, Translate(text), arguments);
}

public sealed record StoredImport(ImportSettings Settings, string UiCulture);
