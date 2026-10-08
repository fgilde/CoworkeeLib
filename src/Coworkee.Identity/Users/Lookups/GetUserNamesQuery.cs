using Coworkee.Application.Messaging;
using Coworkee.Core.Results;

namespace Coworkee.Identity.Users.Lookups;

/// <summary>Display names of users of the own tenant, e.g. for "who" columns.</summary>
public sealed record GetUserNamesQuery(IReadOnlyList<Guid> Ids) : IQuery<Result<IReadOnlyDictionary<Guid, string>>>;
