using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts;
using Coworkee.Contracts.Mailing;
using Coworkee.Core.Results;

namespace Coworkee.Mailing;

[RequiresPermission(MailPermissions.Log.View)]
public sealed record GetOutgoingMails(PageRequest Page, OutgoingMailStatus? Status, Guid? CampaignId = null) : IQuery<Result<PagedResult<OutgoingMailDto>>>;
