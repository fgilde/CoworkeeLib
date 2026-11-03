using System.ComponentModel;
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Reporting;
using Coworkee.Core.Results;

namespace Coworkee.Reporting;

[RequiresPermission(ReportPermissions.View)]
[Description("Reads which report the signed-in user sees as dashboard: the own choice, else the default of the organisation.")]
public sealed record GetDashboardReportQuery : IQuery<Result<DashboardReportDto>>;
