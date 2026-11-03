using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Reporting;
using Coworkee.Core.Results;

namespace Coworkee.Reporting;

[RequiresPermission(ReportPermissions.Share)]
[AiTool(Exclude = true)]
public sealed record GetReportRolesQuery : IQuery<Result<IReadOnlyList<ReportRoleDto>>>;
