using System.ComponentModel;
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Reporting;
using Coworkee.Core.Results;

namespace Coworkee.Reporting;

[RequiresPermission(ReportPermissions.Manage)]
[Description("Replaces name, description, sharing and definition of a report the user owns (e.g. with one from generate_report).")]
public sealed record UpdateReportCommand(Guid Id, SaveReportRequest Report) : ICommand<Result<ReportDto>>;
