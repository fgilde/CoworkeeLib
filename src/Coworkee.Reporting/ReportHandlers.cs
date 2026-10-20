using System.Text.Json;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Reporting;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Reporting.Querying;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Reporting;

internal sealed class ReportHandlers(CoworkeeDbContext db, ReportAccess access, ReportQueries queries, ICurrentUser currentUser, IUserDirectory? users = null)
    : IHandler<GetReportsQuery, Result<IReadOnlyList<ReportSummaryDto>>>,
      IHandler<GetReportQuery, Result<ReportDto>>,
      IHandler<CreateReportCommand, Result<ReportDto>>,
      IHandler<UpdateReportCommand, Result<ReportDto>>,
      IHandler<DeleteReportCommand, Result>,
      IHandler<DuplicateReportCommand, Result<ReportDto>>,
      IHandler<SetReportFavoriteCommand, Result>,
      IHandler<GetReportRolesQuery, Result<IReadOnlyList<ReportRoleDto>>>,
      IHandler<GetReportSourcesQuery, Result<IReadOnlyList<ReportSourceDto>>>,
      IHandler<RunReportQuery, Result<ReportQueryResult>>
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static readonly Error NotFound = Error.NotFound("reports.not_found", "The report does not exist or is not shared with you.");
    private static readonly Error NotOwner = Error.Forbidden("reports.not_owner", "Only the owner changes a report; duplicate it to make your own.");
    private static readonly Error NoShare = Error.Forbidden("reports.no_share", "Sharing reports needs the permission Reports.Share.");

    public async Task<Result<IReadOnlyList<ReportSummaryDto>>> HandleAsync(GetReportsQuery query, CancellationToken cancellationToken)
    {
        var reports = await (await access.VisibleAsync(cancellationToken)).AsNoTracking().ToListAsync(cancellationToken);
        var favorites = await Favorites(reports.Select(r => r.Id)).ToListAsync(cancellationToken);
        var owners = await OwnerNamesAsync(reports, cancellationToken);
        return reports
            .Select(r => (Report: r, Definition: Read(r)))
            .Select(r => new ReportSummaryDto(r.Report.Id, r.Report.Name, r.Report.Description, r.Report.Icon, r.Report.Visibility,
                owners.GetValueOrDefault(r.Report.OwnerId), access.Owns(r.Report), favorites.Contains(r.Report.Id), r.Definition.Widgets.Count,
                [.. r.Definition.Widgets.Select(w => w.Query?.EntitySet).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase)],
                r.Report.ModifiedAt ?? r.Report.CreatedAt))
            .OrderByDescending(r => r.IsFavorite)
            .ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public async Task<Result<ReportDto>> HandleAsync(GetReportQuery query, CancellationToken cancellationToken) =>
        await FindAsync(query.Id, cancellationToken) is { } report ? await ToDtoAsync(report, cancellationToken) : NotFound;

    public async Task<Result<ReportDto>> HandleAsync(CreateReportCommand command, CancellationToken cancellationToken)
    {
        if (await CheckAsync(command.Report, null, cancellationToken) is { } error)
        {
            return error;
        }

        var report = new Report
        {
            TenantId = currentUser.TenantId ?? throw new InvalidOperationException("Reports belong to an organisation."),
            OwnerId = access.Me,
            Name = command.Report.Name.Trim(),
            Definition = Write(command.Report.Definition),
        };
        Apply(report, command.Report);
        db.Add(report);
        await db.SaveChangesAsync(cancellationToken);
        return await ToDtoAsync(report, cancellationToken);
    }

    public async Task<Result<ReportDto>> HandleAsync(UpdateReportCommand command, CancellationToken cancellationToken)
    {
        if (await FindAsync(command.Id, cancellationToken) is not { } report)
        {
            return NotFound;
        }

        if (!access.Owns(report))
        {
            return NotOwner;
        }

        if (await CheckAsync(command.Report, report, cancellationToken) is { } error)
        {
            return error;
        }

        Apply(report, command.Report);
        await db.SaveChangesAsync(cancellationToken);
        return await ToDtoAsync(report, cancellationToken);
    }

    public async Task<Result> HandleAsync(DeleteReportCommand command, CancellationToken cancellationToken)
    {
        if (await FindAsync(command.Id, cancellationToken) is not { } report)
        {
            return NotFound;
        }

        if (!access.Owns(report))
        {
            return NotOwner;
        }

        // the database cascades too; removing them here tells the open dashboards
        db.RemoveRange(await db.Set<DashboardReport>().Where(d => d.ReportId == report.Id).ToListAsync(cancellationToken));
        db.Remove(report);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<ReportDto>> HandleAsync(DuplicateReportCommand command, CancellationToken cancellationToken)
    {
        if (await FindAsync(command.Id, cancellationToken) is not { } original)
        {
            return NotFound;
        }

        var copy = new Report
        {
            TenantId = original.TenantId,
            OwnerId = access.Me,
            Name = $"{original.Name} (copy)"[..Math.Min(ReportLimits.NameLength, original.Name.Length + 7)],
            Description = original.Description,
            Icon = original.Icon,
            Visibility = ReportVisibility.Private,
            Definition = original.Definition,
        };
        db.Add(copy);
        await db.SaveChangesAsync(cancellationToken);
        return await ToDtoAsync(copy, cancellationToken);
    }

    public async Task<Result> HandleAsync(SetReportFavoriteCommand command, CancellationToken cancellationToken)
    {
        if (await FindAsync(command.Id, cancellationToken) is null)
        {
            return NotFound;
        }

        var existing = await db.Set<ReportFavorite>().FirstOrDefaultAsync(f => f.ReportId == command.Id && f.UserId == access.Me, cancellationToken);
        if (command.Favorite && existing is null)
        {
            db.Add(new ReportFavorite { ReportId = command.Id, UserId = access.Me });
        }
        else if (!command.Favorite && existing is not null)
        {
            db.Remove(existing);
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<IReadOnlyList<ReportRoleDto>>> HandleAsync(GetReportRolesQuery query, CancellationToken cancellationToken) =>
        await db.Set<Role>().AsNoTracking()
            .Where(r => r.TenantId == currentUser.TenantId)
            .OrderBy(r => r.Name)
            .Select(r => new ReportRoleDto(r.Id, r.Name!))
            .ToListAsync(cancellationToken);

    public async Task<Result<IReadOnlyList<ReportSourceDto>>> HandleAsync(GetReportSourcesQuery query, CancellationToken cancellationToken) =>
        (await queries.SourcesAsync(cancellationToken))
            .Select(s => new ReportSourceDto(s.EntitySet, s.Label, [.. s.Fields.Select(f => f.ToDto())],
                s.Computed is null && s.RowType.IsDefined(typeof(Domain.RealtimeAttribute), false) ? Contracts.Realtime.RealtimeTopics.Type(s.RowType.Name) : null, s.Computed is not null))
            .OrderBy(s => s.Computed)
            .ThenBy(s => s.Label, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    public Task<Result<ReportQueryResult>> HandleAsync(RunReportQuery query, CancellationToken cancellationToken) => queries.RunAsync(query.Request, cancellationToken);

    /// <summary>Sharing needs its permission, and every widget has to work for the author.</summary>
    private async Task<Error?> CheckAsync(SaveReportRequest request, Report? existing, CancellationToken cancellationToken)
    {
        var sharing = request.Visibility != ReportVisibility.Private
            && (existing is null || existing.Visibility != request.Visibility || !existing.RoleIds.Order().SequenceEqual(request.RoleIds.Order()));
        if (sharing && !await access.MayShareAsync(cancellationToken))
        {
            return NoShare;
        }

        var sources = await queries.SourcesAsync(cancellationToken);
        foreach (var widget in request.Definition.Widgets.Where(w => w.Query is not null && w.Type is not (WidgetType.Markdown or WidgetType.Image or WidgetType.Embed)))
        {
            if (await queries.CheckAsync(ReportQueryRequest.From(widget.Query!, widget.Query!.Filter), cancellationToken) is { } error)
            {
                return error.Kind == ErrorKind.Validation && error.Details is { } details
                    ? Error.Validation(details.ToDictionary(d => $"Widgets[{widget.Id}].{d.Key}", d => d.Value, StringComparer.Ordinal))
                    : error with { Message = $"{widget.Title ?? widget.Id}: {error.Message}" };
            }

            foreach (var column in widget.Query.Columns.Concat(new[] { widget.Query.DateField, widget.Query.OrderBy }.OfType<string>()))
            {
                if (sources.FirstOrDefault(s => string.Equals(s.EntitySet, widget.Query.EntitySet, StringComparison.OrdinalIgnoreCase))?.Find(column) is null)
                {
                    return Error.Validation($"Widgets[{widget.Id}].Columns", $"{widget.Query.EntitySet} has no field '{column}'.");
                }
            }
        }

        return null;
    }

    private static void Apply(Report report, SaveReportRequest request)
    {
        report.Name = request.Name.Trim();
        report.Description = request.Description?.Trim();
        report.Icon = request.Icon;
        report.Visibility = request.Visibility;
        report.RoleIds = request.Visibility == ReportVisibility.Roles ? [.. request.RoleIds.Distinct()] : [];
        report.Definition = Write(request.Definition);
    }

    private async Task<Report?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        await (await access.VisibleAsync(cancellationToken)).FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    private IQueryable<Guid> Favorites(IEnumerable<Guid> ids)
    {
        var me = access.Me;
        var list = ids.ToList();
        return db.Set<ReportFavorite>().Where(f => f.UserId == me && list.Contains(f.ReportId)).Select(f => f.ReportId);
    }

    private async Task<ReportDto> ToDtoAsync(Report report, CancellationToken cancellationToken)
    {
        var favorite = await Favorites([report.Id]).AnyAsync(cancellationToken);
        var owners = await OwnerNamesAsync([report], cancellationToken);
        return new ReportDto(report.Id, report.Name, report.Description, report.Icon, report.Visibility, report.RoleIds, owners.GetValueOrDefault(report.OwnerId),
            access.Owns(report), favorite, Read(report), report.ModifiedAt ?? report.CreatedAt);
    }

    private async Task<IReadOnlyDictionary<Guid, string>> OwnerNamesAsync(IReadOnlyCollection<Report> reports, CancellationToken cancellationToken) =>
        users is null || reports.Count == 0
            ? new Dictionary<Guid, string>()
            : await users.GetDisplayNamesAsync([.. reports.Select(r => r.OwnerId).Distinct()], cancellationToken);

    internal static ReportDefinition Read(Report report) => JsonSerializer.Deserialize<ReportDefinition>(report.Definition, Json) ?? new ReportDefinition();

    internal static string Write(ReportDefinition definition) => JsonSerializer.Serialize(definition, Json);
}
