using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Core.Results;

namespace Coworkee.Account.Email;

/// <summary>Sends the link that makes <paramref name="NewEmail"/> the own address; the current one stays until it is opened.</summary>
[AiTool(Exclude = true)]
[NotWhileImpersonating]
public sealed record ChangeMyEmail(string NewEmail, string? CurrentPassword) : ICommand<Result>;
