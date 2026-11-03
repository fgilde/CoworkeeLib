using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Reporting;
using Coworkee.Core.Results;

namespace Coworkee.Reporting;

[RequiresPermission(ReportPermissions.Manage)]
[AiTool(Exclude = true)]
public sealed record GetReportAiStatusQuery : IQuery<Result<ReportAiStatusDto>>;
