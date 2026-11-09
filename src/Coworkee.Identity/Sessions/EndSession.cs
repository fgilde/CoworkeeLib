using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Core.Results;

namespace Coworkee.Identity.Sessions;

/// <summary>Ends one session: one of the caller's others, or with <c>Identity.Sessions.Manage</c> one of a user in reach (checked in the handler).</summary>
// an impersonator acts as the user, he does not end the user's own sign-ins
[AiTool(Exclude = true)]
[NotWhileImpersonating]
public sealed record EndSession(Guid Id) : ICommand<Result>;
