using System.ComponentModel;
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Reporting;
using Coworkee.Core.Results;

namespace Coworkee.Reporting;

[RequiresPermission(ReportPermissions.View)]
[Description("Lists the reports and dashboards the signed-in user can open, favorites first.")]
public sealed record GetReportsQuery : IQuery<Result<IReadOnlyList<ReportSummaryDto>>>;
