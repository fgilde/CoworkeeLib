using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;

namespace Coworkee.Identity.Groups;

/// <summary>The default group of the tenant; every signed-in user may ask, apps need it in their filters.</summary>
public sealed record GetDefaultGroup : IQuery<Result<DefaultGroupDto>>;
