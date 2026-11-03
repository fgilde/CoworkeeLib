using System.ComponentModel;
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Data;
using Coworkee.Core.Results;

namespace Coworkee.OData.Transfer;

[RequiresPermission(DataTransferPermissions.ExportSchedules)]
[Description("Deletes one of the user's scheduled list exports.")]
public sealed record DeleteExportScheduleCommand(Guid Id) : ICommand<Result>;
