using System.Linq.Expressions;

namespace Coworkee.OData;

/// <summary>Which properties of an OData set stay out of reach, for everyone or for users without a permission.</summary>
public sealed class ODataEntityOptions<TEntity>
    where TEntity : class
{
    internal List<string> Hidden { get; } = [];

    internal Dictionary<string, string> Protected { get; } = [];

    internal string? KeyName { get; private set; }

    /// <summary>
    /// The key of the set when it is not <c>Id</c>, e.g. <c>o =&gt; o.Key(c =&gt; c.IsoCode)</c>: any primitive type, by-key reads are
    /// <c>/odata/Countries('DE')</c>. Features that need a Guid <c>Id</c> (client entities, attachments, social, workflows, history, webhooks) refuse such an entity.
    /// </summary>
    public ODataEntityOptions<TEntity> Key(Expression<Func<TEntity, object?>> property)
    {
        KeyName = ODataServiceCollectionExtensions.PropertyName(property);
        return this;
    }

    /// <summary>Out of the EDM for everyone: never serialized, filtered or sorted on.</summary>
    public ODataEntityOptions<TEntity> Hide(params Expression<Func<TEntity, object?>>[] properties)
    {
        Hidden.AddRange(properties.Select(ODataServiceCollectionExtensions.PropertyName));
        return this;
    }

    /// <summary>
    /// Only users with <paramref name="permission"/> see the property. For everyone else the lists leave it out, and $filter, $orderby, $select,
    /// $expand and facet filters naming it are refused; facets, exports, reports, the global search, the data tools of the assistant and the
    /// entity history leave it out too.
    /// </summary>
    public ODataEntityOptions<TEntity> HideUnless(Expression<Func<TEntity, object?>> property, string permission)
    {
        Protected[ODataServiceCollectionExtensions.PropertyName(property)] = permission;
        return this;
    }
}
