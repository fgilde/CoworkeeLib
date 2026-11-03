using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;

namespace Coworkee.Identity.Users;

public sealed record GetMyProfile : IQuery<Result<ProfileDto>>;
