using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;

namespace Coworkee.Identity.Sessions;

/// <summary>The caller's active sessions, his own marked; every signed-in user sees his.</summary>
[AiTool(Exclude = true)]
public sealed record GetMySessions : IQuery<Result<IReadOnlyList<UserSessionDto>>>;
