using System.ComponentModel;
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Reporting;
using Coworkee.Core.Results;

namespace Coworkee.Reporting.Subscriptions;

[RequiresPermission(ReportPermissions.View)]
[Description("Lists the signed-in user's report subscriptions (reports mailed on a schedule) with their next and last run.")]
public sealed record GetMyReportSubscriptionsQuery : IQuery<Result<IReadOnlyList<ReportSubscriptionDto>>>;
