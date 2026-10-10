namespace Coworkee.Domain;

public interface IAuditable
{
    DateTimeOffset CreatedAt { get; set; }

    Guid? CreatedBy { get; set; }

    DateTimeOffset? ModifiedAt { get; set; }

    Guid? ModifiedBy { get; set; }
}

public interface ISoftDelete
{
    bool IsDeleted { get; set; }

    DateTimeOffset? DeletedAt { get; set; }

    Guid? DeletedBy { get; set; }
}

public interface IMultiTenant
{
    Guid TenantId { get; set; }
}

public interface IHasConcurrencyToken
{
    uint Version { get; set; }
}

public interface IVersioned
{
    int Revision { get; set; }
}

/// <summary>
/// Clients may mirror the entity's OData set and query it locally once it arrived (Coworkee.OData sync feed).
/// <see cref="MaxRows"/> above zero overrides the default limit beyond which clients keep asking the server.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ClientEntityAttribute : Attribute
{
    public int MaxRows { get; init; }
}

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class RealtimeAttribute(string permission) : Attribute
{
    public string Permission { get; } = permission;

    public string? ResourceType { get; init; }
}

public interface IHasRealtimeTopics
{
    IEnumerable<string> RealtimeTopics { get; }
}

public interface IRealtimeTopicMapper
{
    IEnumerable<string> TopicsFor(object entity);
}
