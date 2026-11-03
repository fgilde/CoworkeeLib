using System.ComponentModel;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Notifications;
using Coworkee.Core.Results;

namespace Coworkee.Notifications;

[Description("Whether the user follows an entity and may follow it.")]
public sealed record GetFollowState(string EntityType, Guid EntityId) : IQuery<Result<FollowStateDto>>;
