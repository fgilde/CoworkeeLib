using Coworkee.Application.Privacy;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Webhooks;

/// <summary>
/// The endpoints the user saved last. They belong to the organisation's integrations, so erasing keeps them, but switched off: deliveries ran with his
/// permissions, which are gone. Saving such an endpoint again makes the saving admin its owner and switches it back on.
/// </summary>
internal sealed class WebhookPersonalData(CoworkeeDbContext db, TimeProvider clock) : IPersonalDataContributor
{
    public string Section => "webhooks";

    public async Task<object?> ExportAsync(PersonalDataSubject subject, CancellationToken cancellationToken) =>
        await Owned(subject).AsNoTracking().OrderBy(s => s.CreatedAt)
            .Select(s => new { s.Url, s.Description, s.Events, s.IsActive, s.CreatedAt, s.ModifiedAt })
            .ToListAsync(cancellationToken);

    public Task EraseAsync(PersonalDataSubject subject, CancellationToken cancellationToken)
    {
        var now = (DateTimeOffset?)clock.GetUtcNow();
        return Owned(subject).ExecuteUpdateAsync(
            s => s.SetProperty(w => w.OwnerId, PersonalDataSubject.ErasedUserId).SetProperty(w => w.IsActive, false).SetProperty(w => w.DisabledAt, now),
            cancellationToken);
    }

    private IQueryable<WebhookSubscription> Owned(PersonalDataSubject subject) => db.Set<WebhookSubscription>().IgnoreQueryFilters().Where(s => s.OwnerId == subject.UserId);
}
