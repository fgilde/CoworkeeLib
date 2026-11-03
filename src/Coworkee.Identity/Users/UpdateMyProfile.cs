using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;

namespace Coworkee.Identity.Users;

public sealed record UpdateMyProfile(UpdateProfileRequest Profile) : ICommand<Result<ProfileDto>>;
