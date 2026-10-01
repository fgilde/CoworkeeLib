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

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class RealtimeAttribute(string permission) : Attribute
{
    public string Permission { get; } = permission;
}

public interface IHasRealtimeTopics
{
    IEnumerable<string> RealtimeTopics { get; }
}

public interface IRealtimeTopicMapper
{
    IEnumerable<string> TopicsFor(object entity);
}
