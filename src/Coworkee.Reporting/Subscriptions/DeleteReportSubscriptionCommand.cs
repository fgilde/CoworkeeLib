using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Reporting;
using Coworkee.Core.Results;

namespace Coworkee.Reporting.Subscriptions;

/// <summary>Ends one's own subscription; holders of <see cref="ReportPermissions.Subscriptions"/> end anyone's.</summary>
[RequiresPermission(ReportPermissions.View)]
[AiTool(Exclude = true)]
public sealed record DeleteReportSubscriptionCommand(Guid Id) : ICommand<Result>;
