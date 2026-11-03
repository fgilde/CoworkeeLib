using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Backup;
using Coworkee.Core.Results;

namespace Coworkee.Backup;

[AiTool(Exclude = true)]
[RequiresPermission(BackupPermissions.Manage)]
public sealed record RestoreBackupCommand(Guid Id) : ICommand<Result>;
