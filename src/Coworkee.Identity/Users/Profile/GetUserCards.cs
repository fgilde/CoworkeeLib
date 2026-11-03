using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;

namespace Coworkee.Identity.Users.Profile;

/// <summary>Names and avatar versions of users of the own organisation, e.g. for user cards in lists and chats.</summary>
public sealed record GetUserCards(IReadOnlyList<Guid> Ids) : IQuery<Result<IReadOnlyList<UserCardDto>>>;
