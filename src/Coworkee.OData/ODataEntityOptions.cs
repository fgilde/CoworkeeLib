using System.Linq.Expressions;

namespace Coworkee.OData;

/// <summary>Which properties of an OData set stay out of reach, for everyone or for users without a permission.</summary>
public sealed class ODataEntityOptions<TEntity>
    where TEntity : class
{
    internal List<string> Hidden { get; } = [];

    internal Dictionary<string, string> Protected { get; } = [];

    internal string? KeyName { get; private set; }

    internal int? NodeLimit { get; private set; }

    internal int? FacetRows { get; private set; }

    internal List<Expression<Func<TEntity, string?>>> Searched { get; } = [];

    internal List<string> Indexed { get; } = [];

    /// <summary>
    /// Fields the database has an index for that the EF model does not declare, e.g. <c>o =&gt; o.IndexedFields(s =&gt; s.HouseBill)</c> for a table of a
    /// foreign database mapped without its indexes. The assistant's <c>list_data_sets</c> names them with the keys and indexes of the EF model, so it
    /// filters on them when it can.
    /// </summary>
    public ODataEntityOptions<TEntity> IndexedFields(params Expression<Func<TEntity, object?>>[] properties)
    {
        Indexed.AddRange(properties.Select(ODataServiceCollectionExtensions.PropertyName));
        return this;
    }

    /// <summary>
    /// The text fields searched in this set, e.g. <c>o =&gt; o.SearchFields(p =&gt; p.Name, p =&gt; p.Barcode)</c>: <c>$search</c>, the search box of every data table of
    /// the set that names no own SearchFields, and the global search of the set when AddGlobalSearch names no fields. Fields may read through navigations
    /// (<c>p =&gt; p.Brand!.Name</c>). An entity implementing ISearchable keeps its own.
    /// </summary>
    public ODataEntityOptions<TEntity> SearchFields(params Expression<Func<TEntity, string?>>[] fields)
    {
        Searched.AddRange(fields);
        return this;
    }

    /// <summary>
    /// The facet cap of this set instead of <c>Coworkee:OData:MaxFacetRows</c>: a filtered set of more rows counts its facets on its first
    /// <paramref name="rows"/> rows (by key) instead of all, and says so (<c>FacetGroupDto.SampleRows</c>), so a large list still shows its filters.
    /// </summary>
    public ODataEntityOptions<TEntity> MaxFacetRows(int rows)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(rows, 1);
        FacetRows = rows;
        return this;
    }

    /// <summary>
    /// Nodes a <c>$filter</c> or <c>$orderby</c> of this set may have, instead of <c>Coworkee:OData:MaxNodeCount</c>: for a set whose clients send long
    /// filters (a dashboard ORing many chosen values). It applies wherever the set's queries are checked: the routes, <see cref="IODataQueries"/>, exports,
    /// facet filters, row filters of code and the assistant's data tools.
    /// </summary>
    public ODataEntityOptions<TEntity> MaxNodeCount(int nodes)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(nodes, 1);
        NodeLimit = nodes;
        return this;
    }

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
