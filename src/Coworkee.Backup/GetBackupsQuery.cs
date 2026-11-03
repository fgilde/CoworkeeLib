using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Backup;
using Coworkee.Core.Results;

namespace Coworkee.Backup;

[AiTool(Exclude = true)]
[RequiresPermission(BackupPermissions.Manage)]
public sealed record GetBackupsQuery : IQuery<Result<IReadOnlyList<BackupDto>>>;
