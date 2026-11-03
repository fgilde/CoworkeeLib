using System.ComponentModel;
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Data;
using Coworkee.Core.Results;

namespace Coworkee.OData.Transfer;

[RequiresPermission(DataTransferPermissions.ExportSchedules)]
[Description("Creates (no id) or changes a scheduled list export that is mailed to the user and further recipients.")]
public sealed record SaveExportScheduleCommand(Guid? Id, SaveExportScheduleRequest Schedule) : ICommand<Result<ExportScheduleDto>>;
