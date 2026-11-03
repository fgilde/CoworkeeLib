using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Core.Results;

namespace Coworkee.Account.Password;

/// <summary>
/// The own new password, with the rules of the account page: password policy and history, wrong current passwords count towards the lockout,
/// a new security stamp ends the other sessions while the calling one keeps its tokens.
/// </summary>
[AiTool(Exclude = true)]
[NotWhileImpersonating]
public sealed record ChangeMyPassword(string CurrentPassword, string NewPassword) : ICommand<Result>;
