using System.ComponentModel;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Notifications;
using Coworkee.Core.Results;

namespace Coworkee.Notifications;

[Description("Lists the entities the user follows, newest first.")]
public sealed record GetFollows : IQuery<Result<IReadOnlyList<FollowDto>>>;
