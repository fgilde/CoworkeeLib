using System.Text.Json;
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Reporting;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Reporting.Subscriptions;

internal sealed class ReportSubscriptionHandlers(
    CoworkeeDbContext db, ReportAccess access, ReportSubscriptionMail mail, IPermissionChecker permissions, ICurrentUser currentUser, TimeProvider clock, IUserDirectory? users = null)
    : IHandler<GetMyReportSubscriptionsQuery, Result<IReadOnlyList<ReportSubscriptionDto>>>,
      IHandler<GetAllReportSubscriptionsQuery, Result<IReadOnlyList<ReportSubscriptionDto>>>,
      IHandler<CreateReportSubscriptionCommand, Result<ReportSubscriptionDto>>,
      IHandler<UpdateReportSubscriptionCommand, Result<ReportSubscriptionDto>>,
      IHandler<DeleteReportSubscriptionCommand, Result>,
      IHandler<SendReportSubscriptionNowCommand, Result<ReportSubscriptionDto>>
{
    public const int PerUser = 50;

    private static readonly Error NotFound = Error.NotFound("reports.subscription_not_found", "The subscription does not exist.");
    private static readonly Error ReportNotFound = Error.NotFound("reports.not_found", "The report does not exist or is not shared with you.");

    public async Task<Result<IReadOnlyList<ReportSubscriptionDto>>> HandleAsync(GetMyReportSubscriptionsQuery query, CancellationToken cancellationToken) =>
        await ListAsync(db.Set<ReportSubscription>().Where(s => s.UserId == access.Me), cancellationToken);

    public async Task<Result<IReadOnlyList<ReportSubscriptionDto>>> HandleAsync(GetAllReportSubscriptionsQuery query, CancellationToken cancellationToken) =>
        await ListAsync(db.Set<ReportSubscription>(), cancellationToken);

    public async Task<Result<ReportSubscriptionDto>> HandleAsync(CreateReportSubscriptionCommand command, CancellationToken cancellationToken)
    {
        var request = command.Subscription;
        if (!await (await access.VisibleAsync(cancellationToken)).AnyAsync(r => r.Id == request.ReportId, cancellationToken))
        {
            return ReportNotFound;
        }

        var me = access.Me;
        if (await db.Set<ReportSubscription>().CountAsync(s => s.UserId == me, cancellationToken) >= PerUser)
        {
            return Error.Validation(nameof(command.Subscription), $"At most {PerUser} subscriptions per user.");
        }

        var subscription = new ReportSubscription
        {
            TenantId = currentUser.TenantId ?? throw new InvalidOperationException("Reports belong to an organisation."),
            UserId = me,
            ReportId = request.ReportId,
            TimeZone = request.Schedule.TimeZone,
            CreatedAt = clock.GetUtcNow(),
        };
        Apply(subscription, request);
        db.Add(subscription);
        await db.SaveChangesAsync(cancellationToken);
        return (await ListAsync(db.Set<ReportSubscription>().Where(s => s.Id == subscription.Id), cancellationToken)).Single();
    }

    public async Task<Result<ReportSubscriptionDto>> HandleAsync(UpdateReportSubscriptionCommand command, CancellationToken cancellationToken)
    {
        if (await MineAsync(command.Id, cancellationToken) is not { } subscription)
        {
            return NotFound;
        }

        if (subscription.ReportId != command.Subscription.ReportId)
        {
            return Error.Validation(nameof(SaveReportSubscriptionRequest.ReportId), "A subscription stays with its report.");
        }

        Apply(subscription, command.Subscription);
        await db.SaveChangesAsync(cancellationToken);
        return (await ListAsync(db.Set<ReportSubscription>().Where(s => s.Id == subscription.Id), cancellationToken)).Single();
    }

    public async Task<Result> HandleAsync(DeleteReportSubscriptionCommand command, CancellationToken cancellationToken)
    {
        var subscription = await MineAsync(command.Id, cancellationToken)
            ?? (await permissions.IsGrantedAsync(ReportPermissions.Subscriptions, cancellationToken)
                ? await db.Set<ReportSubscription>().FirstOrDefaultAsync(s => s.Id == command.Id, cancellationToken)
                : null);
        if (subscription is null)
        {
            return NotFound;
        }

        db.Remove(subscription);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<ReportSubscriptionDto>> HandleAsync(SendReportSubscriptionNowCommand command, CancellationToken cancellationToken)
    {
        if (await MineAsync(command.Id, cancellationToken) is null)
        {
            return NotFound;
        }

        await mail.RunAsync(command.Id, clock.GetUtcNow(), advance: false, cancellationToken);
        return (await ListAsync(db.Set<ReportSubscription>().Where(s => s.Id == command.Id), cancellationToken)).Single();
    }

    private void Apply(ReportSubscription subscription, SaveReportSubscriptionRequest request)
    {
        subscription.Apply(request.Schedule, clock.GetUtcNow());
        subscription.Attachment = request.Attachment;
        subscription.Filter = request.DateRange is null && request.Filters is null
            ? null
            : JsonSerializer.Serialize(new ReportSubscriptionFilter(request.DateRange, request.Filters), ReportHandlers.Json);
        subscription.LastError = null;
    }

    private Task<ReportSubscription?> MineAsync(Guid id, CancellationToken cancellationToken)
    {
        var me = access.Me;
        return db.Set<ReportSubscription>().FirstOrDefaultAsync(s => s.Id == id && s.UserId == me, cancellationToken);
    }

    private async Task<List<ReportSubscriptionDto>> ListAsync(IQueryable<ReportSubscription> subscriptions, CancellationToken cancellationToken)
    {
        var rows = await subscriptions.AsNoTracking()
            .Join(db.Set<Report>(), s => s.ReportId, r => r.Id, (s, r) => new { Subscription = s, r.Name })
            .ToListAsync(cancellationToken);
        var names = users is null || rows.Count == 0
            ? new Dictionary<Guid, string>()
            : await users.GetDisplayNamesAsync([.. rows.Select(r => r.Subscription.UserId).Distinct()], cancellationToken);
        return
        [
            .. rows.OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(r => r.Subscription.NextRunAt).Select(r =>
            {
                var s = r.Subscription;
                var filter = s.Filter is { Length: > 0 } json ? JsonSerializer.Deserialize<ReportSubscriptionFilter>(json, ReportHandlers.Json) : null;
                return new ReportSubscriptionDto(s.Id, s.ReportId, r.Name, s.UserId, names.GetValueOrDefault(s.UserId), s.Schedule, filter?.DateRange, filter?.Filters, s.Attachment,
                    s.NextRunAt, s.LastSentAt, s.LastError);
            }),
        ];
    }
}
