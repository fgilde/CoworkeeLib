using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Ai;
using Coworkee.Core.Results;

namespace Coworkee.Ai.Sessions;

[AiTool(Exclude = true)]
[RequiresPermission(AiPermissions.Chat)]
public sealed record CreateAiSession(string? Title) : ICommand<Result<AiSessionDto>>;
