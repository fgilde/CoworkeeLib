using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;

namespace Coworkee.Identity.Users.Profile;

/// <summary>Sets or (with null) removes the own profile picture.</summary>
[AiTool(Exclude = true)]
public sealed record SetMyAvatar(string? DataUrl) : ICommand<Result<ProfileDto>>;
