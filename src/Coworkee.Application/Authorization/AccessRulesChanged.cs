using Coworkee.Domain;

namespace Coworkee.Application.Authorization;

/// <summary>The permissions of these roles changed (or the roles are gone), so every resource grant using them may mean something else now; raised in the roles' tenant, or without tenant for roles shared by all tenants.</summary>
public sealed record AccessRulesChanged(Guid[] RoleIds) : IDomainEvent;
