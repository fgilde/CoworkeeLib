using System.ComponentModel;
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Reporting;
using Coworkee.Core.Results;

namespace Coworkee.Reporting;

[RequiresPermission(ReportPermissions.Manage)]
[Description("Saves a new report of the user with name, description, sharing and definition (e.g. one from generate_report).")]
public sealed record CreateReportCommand(SaveReportRequest Report) : ICommand<Result<ReportDto>>;
