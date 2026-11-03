using Coworkee.Application.Messaging;
using Coworkee.Core.Results;

namespace Coworkee.Identity.Groups;

/// <summary>Replaces the current user's selection; empty selects nothing (all accessible groups apply).</summary>
public sealed record SelectGroups(IReadOnlyList<Guid> GroupIds) : ICommand<Result>;
