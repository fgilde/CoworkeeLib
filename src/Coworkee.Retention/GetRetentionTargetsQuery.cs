using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Retention;
using Coworkee.Core.Results;

namespace Coworkee.Retention;

/// <summary>Without <paramref name="Counts"/> only the days and runs, for following a run.</summary>
[AiTool(Exclude = true)]
[RequiresPermission(RetentionPermissions.Manage)]
public sealed record GetRetentionTargetsQuery(bool Counts = true) : IQuery<Result<IReadOnlyList<RetentionTargetDto>>>;
