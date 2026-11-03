using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;

namespace Coworkee.Identity.Groups;

/// <summary>The groups of the current user's picker: every accessible group; with ScopeAll only memberships, the selection and their ancestors (the rest loads lazily).</summary>
public sealed record GetMyGroups : IQuery<Result<IReadOnlyList<MyGroupDto>>>;
