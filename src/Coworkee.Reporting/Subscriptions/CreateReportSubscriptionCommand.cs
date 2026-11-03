using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Reporting;
using Coworkee.Core.Results;

namespace Coworkee.Reporting.Subscriptions;

[RequiresPermission(ReportPermissions.View)]
[AiTool(Exclude = true)]
[NotWhileImpersonating]
public sealed record CreateReportSubscriptionCommand(SaveReportSubscriptionRequest Subscription) : ICommand<Result<ReportSubscriptionDto>>;
