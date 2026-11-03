using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;

namespace Coworkee.Identity.Groups;

/// <summary>The direct children of a group (the roots for null), by name. Groups.View or Groups.ScopeAll (the scope picker of an operator).</summary>
public sealed record GetGroupChildren(Guid? ParentId) : IQuery<Result<IReadOnlyList<GroupNodeDto>>>;
