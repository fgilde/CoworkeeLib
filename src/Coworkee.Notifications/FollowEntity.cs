using System.ComponentModel;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Notifications;
using Coworkee.Core.Results;

namespace Coworkee.Notifications;

[Description("Follows an entity the user sees: its notifications reach him when his preference is Followed or All.")]
public sealed record FollowEntity(string EntityType, Guid EntityId, string? Title = null) : ICommand<Result<FollowStateDto>>;
