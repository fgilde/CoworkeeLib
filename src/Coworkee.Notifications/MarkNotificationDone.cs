using Coworkee.Application.Messaging;
using Coworkee.Core.Results;

namespace Coworkee.Notifications;

/// <summary>Done marks it read too; undone brings it back into the open inbox.</summary>
public sealed record MarkNotificationDone(Guid Id, bool Done) : ICommand<Result>;
