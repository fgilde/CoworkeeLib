using System.ComponentModel;
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Data;
using Coworkee.Core.Results;

namespace Coworkee.OData.Transfer;

[RequiresPermission(DataTransferPermissions.ExportSchedules)]
[Description("Lists the user's scheduled list exports, optionally of one entity set.")]
public sealed record GetExportSchedulesQuery(string? EntitySet = null) : IQuery<Result<IReadOnlyList<ExportScheduleDto>>>;
