using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts;
using Coworkee.Contracts.Ai;
using Coworkee.Core.Results;

namespace Coworkee.Ai;

[Coworkee.Application.Messaging.AiTool(Exclude = true)]
[RequiresPermission(AiPermissions.Audit)]
public sealed record GetAiToolCalls(PageRequest Page, string? Channel) : IQuery<Result<PagedResult<AiToolCallDto>>>;
