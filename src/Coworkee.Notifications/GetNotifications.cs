using Coworkee.Application.Messaging;
using Coworkee.Contracts;
using Coworkee.Contracts.Notifications;
using Coworkee.Core.Results;

namespace Coworkee.Notifications;

/// <param name="Done">Null: open and done ones; false: open only; true: done only. <see cref="PageRequest.Search"/> looks in title and body.</param>
public sealed record GetNotifications(bool UnreadOnly, PageRequest Page, string? Type = null, string? Category = null, bool? Done = null) : IQuery<Result<PagedResult<NotificationDto>>>;
