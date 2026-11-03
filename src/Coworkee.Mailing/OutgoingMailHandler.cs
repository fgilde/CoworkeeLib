using Coworkee.Application.Messaging;
using Coworkee.Contracts;
using Coworkee.Contracts.Mailing;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Mailing;

internal sealed class OutgoingMailHandler(CoworkeeDbContext db, ICurrentUser currentUser) : IHandler<GetOutgoingMails, Result<PagedResult<OutgoingMailDto>>>
{
    public async Task<Result<PagedResult<OutgoingMailDto>>> HandleAsync(GetOutgoingMails query, CancellationToken cancellationToken)
    {
        var tenantId = currentUser.TenantId;
        var mails = db.Set<OutgoingMail>().AsNoTracking().Where(m => m.TenantId == tenantId);
        if (query.Status is { } status)
        {
            mails = mails.Where(m => m.Status == status);
        }

        if (query.CampaignId is { } campaignId)
        {
            mails = mails.Where(m => m.CampaignId == campaignId);
        }

        if (!string.IsNullOrWhiteSpace(query.Page.Search))
        {
            var search = query.Page.Search.Trim();
            mails = mails.Where(m => m.To.Contains(search) || m.Subject.Contains(search));
        }

        var total = await mails.CountAsync(cancellationToken);
        var items = await mails.OrderByDescending(m => m.QueuedAt)
            .Skip((query.Page.Page - 1) * query.Page.PageSize)
            .Take(query.Page.PageSize)
            .Select(m => new OutgoingMailDto(m.Id, m.To, m.Subject, m.TemplateName, m.Status, m.Attempts, m.LastError, m.QueuedAt, m.SentAt, m.CampaignId))
            .ToListAsync(cancellationToken);
        return new PagedResult<OutgoingMailDto>(items, total, query.Page.Page, query.Page.PageSize);
    }
}
