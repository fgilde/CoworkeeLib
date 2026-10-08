using Coworkee.Core.Security;
using Coworkee.OData;

namespace Coworkee.Mailing.OData;

internal sealed class OutgoingMailODataFilter(ICurrentUser currentUser) : IODataEntityFilter<OutgoingMail>
{
    public Task<IQueryable<OutgoingMail>> ApplyAsync(IQueryable<OutgoingMail> query, CancellationToken cancellationToken) =>
        Task.FromResult(query.Where(m => m.TenantId == currentUser.TenantId));
}
