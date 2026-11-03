using System.ComponentModel;
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Reporting;
using Coworkee.Core.Results;

namespace Coworkee.Reporting;

[RequiresPermission(ReportPermissions.Manage)]
[Description("Copies a report the user can open into a new private report of the user.")]
public sealed record DuplicateReportCommand(Guid Id) : ICommand<Result<ReportDto>>;
