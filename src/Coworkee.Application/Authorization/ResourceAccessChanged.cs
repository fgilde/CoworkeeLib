using Coworkee.Domain;

namespace Coworkee.Application.Authorization;

/// <summary>Grants on one resource were added, changed or removed.</summary>
public sealed record ResourceAccessChanged(string ResourceType, Guid ResourceId) : IDomainEvent;
