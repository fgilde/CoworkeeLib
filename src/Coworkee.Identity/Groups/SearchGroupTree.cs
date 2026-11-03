using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;

namespace Coworkee.Identity.Groups;

/// <summary>Groups whose name contains the search text, each with all its ancestors, in tree order. Groups.View or Groups.ScopeAll.</summary>
public sealed record SearchGroupTree(string Search) : IQuery<Result<IReadOnlyList<GroupNodeDto>>>;
