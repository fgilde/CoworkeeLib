using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Retention;
using Coworkee.Core.Results;

namespace Coworkee.Retention;

/// <summary>Days per target key; null or 0 keeps everything.</summary>
[AiTool(Exclude = true)]
[RequiresPermission(RetentionPermissions.Manage)]
public sealed record SetRetentionDaysCommand(IReadOnlyDictionary<string, int?> Days) : ICommand<Result>;
