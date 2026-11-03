using System.ComponentModel;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Notifications;
using Coworkee.Core.Results;

namespace Coworkee.Notifications;

[Description("Stops following an entity.")]
public sealed record UnfollowEntity(string EntityType, Guid EntityId) : ICommand<Result<FollowStateDto>>;
