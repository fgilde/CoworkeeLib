using Coworkee.Application.Messaging;
using Coworkee.Core.Results;

namespace Coworkee.Identity.Users.Profile;

/// <summary>The profile picture of a user of the own organisation.</summary>
public sealed record GetUserAvatar(Guid UserId) : IQuery<Result<UserAvatar>>;
