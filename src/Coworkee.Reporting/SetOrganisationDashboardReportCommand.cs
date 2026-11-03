using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Reporting;
using Coworkee.Core.Results;

namespace Coworkee.Reporting;

[RequiresPermission(ReportPermissions.Dashboard)]
[AiTool(Exclude = true)]
public sealed record SetOrganisationDashboardReportCommand(Guid? ReportId) : ICommand<Result>;
