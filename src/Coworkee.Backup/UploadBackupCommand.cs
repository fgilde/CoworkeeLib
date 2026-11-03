using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Backup;
using Coworkee.Core.Results;

namespace Coworkee.Backup;

/// <summary>A zip of a download, kept like a backup made here; the restore checks whether it fits.</summary>
[AiTool(Exclude = true)]
[RequiresPermission(BackupPermissions.Manage)]
public sealed record UploadBackupCommand(string Name, Stream Content) : ICommand<Result<BackupDto>>;
