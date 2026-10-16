using System.Linq.Expressions;
using Coworkee.Application.GlobalSearch;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.OData.GlobalSearch;

/// <summary>How the global search finds and shows the records of one registered OData set.</summary>
public sealed class ODataSearchOptions<T>
{
    internal List<Expression<Func<T, string?>>> SearchFields { get; } = [];

    internal Func<T, string?>? TitleOf { get; private set; }

    internal Func<T, string?>? SubtitleOf { get; private set; }

    internal Func<T, string?>? UrlOf { get; private set; }

    internal Func<T, string?>? ThumbnailOf { get; private set; }

    internal string? ListPage { get; private set; }

    internal string? ProviderName { get; private set; }

    internal string? GroupName { get; private set; }

    internal string? IconName { get; private set; }

    internal double Boost { get; private set; } = 1;

    /// <summary>The text columns searched: exact and prefix first, then anywhere. The first one is the title unless <see cref="Title"/> says otherwise.</summary>
    public ODataSearchOptions<T> Fields(params Expression<Func<T, string?>>[] fields)
    {
        SearchFields.AddRange(fields);
        return this;
    }

    public ODataSearchOptions<T> Title(Func<T, string?> title) => Set(() => TitleOf = title);

    public ODataSearchOptions<T> Subtitle(Func<T, string?> subtitle) => Set(() => SubtitleOf = subtitle);

    /// <summary>Where a hit leads, e.g. <c>p =&gt; $"/catalog/products/{p.Id}"</c>.</summary>
    public ODataSearchOptions<T> Url(Func<T, string?> url) => Set(() => UrlOf = url);

    /// <summary>The list page of the set; a hit without <see cref="Url"/> opens it searched for the hit's title.</summary>
    public ODataSearchOptions<T> Page(string page) => Set(() => ListPage = page);

    /// <summary>An image URL or a small data URL (larger ones are left out).</summary>
    public ODataSearchOptions<T> Thumbnail(Func<T, string?> thumbnail) => Set(() => ThumbnailOf = thumbnail);

    /// <summary>The provider name and type filter word; the entity set in lower case by default.</summary>
    public ODataSearchOptions<T> Name(string name) => Set(() => ProviderName = name.ToLowerInvariant());

    /// <summary>The heading; the entity set by default.</summary>
    public ODataSearchOptions<T> Group(string group) => Set(() => GroupName = group);

    /// <summary>A Material icon name, e.g. <c>ViewCarousel</c>.</summary>
    public ODataSearchOptions<T> Icon(string icon) => Set(() => IconName = icon);

    /// <summary>Multiplies the scores of this set, see <see cref="IGlobalSearchProvider.Weight"/>.</summary>
    public ODataSearchOptions<T> Weight(double weight) => Set(() => Boost = weight);

    private ODataSearchOptions<T> Set(Action set)
    {
        set();
        return this;
    }
}

public static class ODataGlobalSearchServiceCollectionExtensions
{
    /// <summary>
    /// Lets the global search find records of a set registered with AddODataEntity: same permission, organisation and row filters as its
    /// OData list, e.g. <c>services.AddGlobalSearch&lt;Product&gt;(s =&gt; s.Fields(p =&gt; p.Name, p =&gt; p.Barcode).Subtitle(p =&gt; p.Barcode).Page("/catalog/products"))</c>.
    /// </summary>
    public static IServiceCollection AddGlobalSearch<T>(this IServiceCollection services, Action<ODataSearchOptions<T>> configure)
        where T : class
    {
        var options = new ODataSearchOptions<T>();
        configure(options);
        if (options.SearchFields.Count == 0)
        {
            throw new ArgumentException($"The global search of {typeof(T).Name} needs at least one field.", nameof(configure));
        }

        return services.AddSingleton<IGlobalSearchProvider>(sp => new ODataSearchProvider<T>(sp.GetRequiredService<ODataEntityRegistry>(), options));
    }
}
