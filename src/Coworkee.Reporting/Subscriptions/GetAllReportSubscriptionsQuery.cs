using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Reporting;
using Coworkee.Core.Results;

namespace Coworkee.Reporting.Subscriptions;

[RequiresPermission(ReportPermissions.Subscriptions)]
[AiTool(Exclude = true)]
public sealed record GetAllReportSubscriptionsQuery : IQuery<Result<IReadOnlyList<ReportSubscriptionDto>>>;
