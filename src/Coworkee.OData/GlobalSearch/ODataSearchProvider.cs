using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Coworkee.Application.GlobalSearch;
using Coworkee.Contracts.Search;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;

namespace Coworkee.OData.GlobalSearch;

/// <summary>
/// Searches the rows of an OData set the user may read (<see cref="ODataEntityAccess.VisibleAsync{TEntity}"/>) in two rounds: values that start
/// with the text (or the record with a typed id) come at once, values that contain every word follow.
/// </summary>
internal sealed class ODataSearchProvider<T> : IGlobalSearchProvider
    where T : class
{
    private const int MaxThumbnailLength = 32 * 1024;
    private static readonly MethodInfo ToLower = typeof(string).GetMethod(nameof(string.ToLower), Type.EmptyTypes)!;
    private static readonly MethodInfo StartsWith = typeof(string).GetMethod(nameof(string.StartsWith), [typeof(string)])!;
    private static readonly MethodInfo Contains = typeof(string).GetMethod(nameof(string.Contains), [typeof(string)])!;

    private readonly ODataEntityRegistry _registry;
    private readonly ODataSearchOptions<T> _options;
    private readonly PropertyInfo _id;
    private readonly List<Func<T, string?>> _fields;

    public ODataSearchProvider(ODataEntityRegistry registry, ODataSearchOptions<T> options)
    {
        _registry = registry;
        _options = options;
        _id = typeof(T).GetProperty("Id") ?? throw new InvalidOperationException($"{typeof(T).Name} needs an Id property for the global search.");
        _fields = [.. options.SearchFields.Select(f => f.Compile())];
        var hidden = options.SearchFields.Select(MemberName).Intersect(Registration.HiddenProperties).ToList();
        if (hidden.Count > 0)
        {
            throw new InvalidOperationException($"The global search of {Registration.EntitySet} searches the hidden properties {string.Join(", ", hidden)}.");
        }
    }

    public string Name => _options.ProviderName ?? Registration.EntitySet.ToLowerInvariant();

    public string Group => _options.GroupName ?? Registration.EntitySet;

    public string? Icon => _options.IconName;

    public string? Permission => Registration.Permission;

    public double Weight => _options.Boost;

    private ODataEntityRegistration Registration =>
        _registry.Find(typeof(T)) ?? throw new InvalidOperationException($"{typeof(T).Name} is searched globally but not registered with AddODataEntity.");

    public async IAsyncEnumerable<GlobalSearchHit> SearchAsync(GlobalSearchQuery query, GlobalSearchContext context, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var visible = await ODataEntityAccess.VisibleAsync<T>(context.Services, cancellationToken);
        var denied = await ODataEntityAccess.DeniedPropertiesAsync(context.Services, typeof(T), cancellationToken);
        var fields = Enumerable.Range(0, _fields.Count).Where(i => !denied.Contains(MemberName(_options.SearchFields[i]))).ToList();
        if (fields.Count == 0)
        {
            yield break;
        }

        var text = query.Text.ToLowerInvariant();
        var first = query.Id is { } id
            ? _id.PropertyType == typeof(Guid) ? visible.Where(IdIs(id)) : null
            : visible.Where(AnyField(fields, value => query.Exact ? Expression.Equal(value, Parameter(text)) : Expression.Call(value, StartsWith, Parameter(text))));
        var sent = new HashSet<string>(StringComparer.Ordinal);
        if (first is not null)
        {
            foreach (var hit in await HitsAsync(first, query, fields, context.Limit, cancellationToken))
            {
                sent.Add(hit.Id!);
                yield return hit;
            }
        }

        if (query.Exact || query.Id is not null || sent.Count >= context.Limit)
        {
            yield break;
        }

        // ponytail: the second round reads the first round's rows again and drops them here; a NOT IN of their ids if limits grow
        var rest = query.Words.Aggregate(visible, (rows, word) =>
            rows.Where(AnyField(fields, value => Expression.Call(value, Contains, Parameter(word.ToLowerInvariant())))));
        foreach (var hit in (await HitsAsync(rest, query, fields, context.Limit + sent.Count, cancellationToken)).Where(h => !sent.Contains(h.Id!)).Take(context.Limit - sent.Count))
        {
            yield return hit;
        }
    }

    private async Task<IEnumerable<GlobalSearchHit>> HitsAsync(IQueryable<T> rows, GlobalSearchQuery query, List<int> fields, int take, CancellationToken cancellationToken)
    {
        var found = await rows.OrderBy(_options.SearchFields[fields[0]]).Take(take).ToListAsync(cancellationToken);
        return found.Select(row => Hit(row, query, fields)).OrderByDescending(h => h.Score);
    }

    // fields the user may not read neither match nor show
    private GlobalSearchHit Hit(T row, GlobalSearchQuery query, List<int> fields)
    {
        var values = fields.Select(i => _fields[i](row)).ToList();
        var id = _id.GetValue(row)?.ToString();
        var title = _options.TitleOf?.Invoke(row) ?? values[0] ?? id ?? string.Empty;
        // the database compares in its own way (collation, LOWER); a row it found counts even when the culture here disagrees
        var match = query.Id is not null ? new SearchMatchResult(1, []) : SearchMatch.Best(query, title, values) ?? new SearchMatchResult(0.2, []);
        var thumbnail = _options.ThumbnailOf?.Invoke(row);
        return new GlobalSearchHit(Name, Group, title)
        {
            Subtitle = _options.SubtitleOf?.Invoke(row),
            Url = _options.UrlOf?.Invoke(row) ?? (_options.ListPage is { } page ? PageLink(page, Registration.EntitySet, title) : null),
            EntitySet = Registration.EntitySet,
            Id = id,
            Score = match.Score,
            Highlights = match.Highlights,
            Thumbnail = thumbnail?.Length <= MaxThumbnailLength ? thumbnail : null,
        };
    }

    /// <summary>The list page searched for <paramref name="title"/>, in the URL state of the client's data table (DataTableState).</summary>
    internal static string PageLink(string page, string entitySet, string title)
    {
        var state = JsonSerializer.SerializeToUtf8Bytes(new { search = title, facets = Array.Empty<object>(), hiddenColumns = Array.Empty<string>() });
        var encoded = Convert.ToBase64String(state).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"{page}{(page.Contains('?') ? '&' : '?')}{entitySet.ToLowerInvariant()}={encoded}";
    }

    private Expression<Func<T, bool>> AnyField(List<int> fields, Func<Expression, Expression> test)
    {
        var row = Expression.Parameter(typeof(T), "row");
        var body = fields.Select(i => _options.SearchFields[i])
            .Select(field => test(Expression.Call(ReplacingExpressionVisitor.Replace(field.Parameters[0], row, field.Body), ToLower)))
            .Aggregate(Expression.OrElse);
        return Expression.Lambda<Func<T, bool>>(body, row);
    }

    // a member of a constant becomes a SQL parameter, a bare constant would be inlined into the statement
    private static MemberExpression Parameter(string value) => Expression.Property(Expression.Constant(new Text(value)), nameof(Text.Value));

    private static Expression<Func<T, bool>> IdIs(Guid id)
    {
        var row = Expression.Parameter(typeof(T), "row");
        return Expression.Lambda<Func<T, bool>>(Expression.Equal(Expression.Property(row, "Id"), Expression.Constant(id)), row);
    }

    private static string MemberName(Expression<Func<T, string?>> field) =>
        (field.Body as MemberExpression)?.Member.Name ?? string.Empty;

    private sealed record Text(string Value);
}
