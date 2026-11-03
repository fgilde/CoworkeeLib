using System.ComponentModel;
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Reporting;
using Coworkee.Core.Results;

namespace Coworkee.Reporting;

[RequiresPermission(ReportPermissions.View)]
[Description("Reads a report with its widgets, layout and filters.")]
public sealed record GetReportQuery(Guid Id) : IQuery<Result<ReportDto>>;
