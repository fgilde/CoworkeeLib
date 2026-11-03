using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Mailing;
using Coworkee.Core.Results;

namespace Coworkee.Mailing;

[RequiresPermission(MailPermissions.Campaigns.Send)]
public sealed record ScheduleMailCampaign(Guid Id, ScheduleMailCampaignRequest Request) : ICommand<Result<MailCampaignDto>>;
