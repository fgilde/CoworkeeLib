using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Reporting;
using Coworkee.Core.Results;

namespace Coworkee.Reporting;

[RequiresPermission(ReportPermissions.View)]
[AiTool(Exclude = true)]
public sealed record SetReportFavoriteCommand(Guid Id, bool Favorite) : ICommand<Result>;
