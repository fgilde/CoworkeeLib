namespace Coworkee.Client.Blazor.Navigation;

/// <summary>A page found for an address: render <see cref="Component"/> with <see cref="Values"/>, or go to <see cref="RedirectTo"/>.</summary>
public sealed record PageMatch(CoworkeePage Page, Type Component, IReadOnlyDictionary<string, object?> Values, string? RedirectTo = null);

/// <summary>
/// The addresses of the library's pages: the prefix of admin pages, pages the app replaced or turned off. Links, the navigation and the
/// command palette go through <see cref="Href"/>, which also turns old addresses (e.g. "/admin/users/…" in mails) into the current ones.
/// </summary>
public sealed class CoworkeePages
{
    private readonly IReadOnlyList<CoworkeePage> _pages;
    private readonly PageOptions _options;
    private readonly string _prefix;

    public CoworkeePages(IEnumerable<CoworkeePage> pages, CoworkeeClientOptions options)
    {
        _pages = pages.ToList();
        _options = options.Pages;
        _prefix = options.PagePrefix.Trim('/');
        var unknown = _options.Components.Keys.Concat(_options.Hrefs.Keys).Concat(_options.Disabled)
            .Where(key => _pages.All(p => !p.Key.Equals(key, StringComparison.OrdinalIgnoreCase))).ToList();
        if (unknown.Count > 0)
        {
            throw new InvalidOperationException($"No page of the library has the key {string.Join(", ", unknown)}.");
        }
    }

    public IReadOnlyList<CoworkeePage> All => _pages;

    public bool IsEnabled(string key) => !_options.Disabled.Contains(key);

    /// <summary>The current link for <paramref name="path"/> (old or current address of a library page, query kept); null for a turned off page.</summary>
    public string? Href(string path)
    {
        var (segments, rest) = Split(path);
        foreach (var page in _pages)
        {
            foreach (var route in page.Routes)
            {
                if (TryMatch(Forms(page, route), segments, out var values))
                {
                    return IsEnabled(page.Key) ? "/" + PageRoute.Fill(Current(page, route), values) + rest : null;
                }
            }
        }

        return path;
    }

    /// <summary>What the host renders for <paramref name="path"/>; null when no enabled library page has this address.</summary>
    public PageMatch? Match(string path)
    {
        var (segments, rest) = Split(path);
        foreach (var page in _pages.Where(p => IsEnabled(p.Key)))
        {
            foreach (var route in page.Routes)
            {
                var current = Current(page, route);
                if (!_options.Hrefs.ContainsKey(page.Key) && PageRoute.TryMatch(current, segments, out var values))
                {
                    return new PageMatch(page, _options.Components.GetValueOrDefault(page.Key, page.Component), values);
                }

                if (TryMatch(Forms(page, route).Where(f => f != current), segments, out values))
                {
                    return new PageMatch(page, page.Component, values, "/" + PageRoute.Fill(current, values) + rest);
                }
            }
        }

        return null;
    }

    private string Current(CoworkeePage page, string route) =>
        _options.Hrefs.TryGetValue(page.Key, out var href) ? href.Trim('/') : Prefixed(page, route);

    private string Prefixed(CoworkeePage page, string route) => page.IsAdmin && _prefix.Length > 0 ? $"{_prefix}/{route}" : route;

    // every address the page had or has: the old route, the prefixed one and the app's own page
    private IEnumerable<string> Forms(CoworkeePage page, string route) => new[] { route, Prefixed(page, route), Current(page, route) }.Distinct();

    private static bool TryMatch(IEnumerable<string> forms, string[] segments, out IReadOnlyDictionary<string, object?> values)
    {
        foreach (var form in forms)
        {
            if (PageRoute.TryMatch(form, segments, out values))
            {
                return true;
            }
        }

        values = new Dictionary<string, object?>();
        return false;
    }

    private static (string[] Segments, string Tail) Split(string path)
    {
        var cut = path.IndexOfAny(['?', '#']);
        var rest = cut < 0 ? string.Empty : path[cut..];
        var bare = (cut < 0 ? path : path[..cut]).Trim('/');
        return (bare.Length == 0 ? [] : bare.Split('/').Select(Uri.UnescapeDataString).ToArray(), rest);
    }
}
