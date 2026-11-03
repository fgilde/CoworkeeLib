using System.ComponentModel;
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Data;
using Coworkee.Core.Results;

namespace Coworkee.OData.Transfer;

[RequiresPermission(DataTransferPermissions.ExportSchedules)]
[Description("Runs one of the user's scheduled list exports now; the schedule stays.")]
public sealed record RunExportScheduleCommand(Guid Id) : ICommand<Result<ExportScheduleDto>>;
