using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Core.Results;

namespace Coworkee.Identity.Users.Privacy;

/// <summary>Erases the signed-in user's data and account; the e-mail address confirms it.</summary>
[AiTool(Exclude = true)]
[NotWhileImpersonating]
public sealed record DeleteMyAccount(string Email) : ICommand<Result>;
