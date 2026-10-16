namespace Coworkee.Client.Blazor.Navigation;

/// <summary>Replaces or removes pages of the library by their key (see the docs for the list), e.g. Replace("users", typeof(MyUsers)).</summary>
public sealed class PageOptions
{
    internal Dictionary<string, Type> Components { get; } = new(StringComparer.OrdinalIgnoreCase);

    internal Dictionary<string, string> Hrefs { get; } = new(StringComparer.OrdinalIgnoreCase);

    internal HashSet<string> Disabled { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Renders <paramref name="component"/> at the library's address; it gets the same route parameters (e.g. Id).</summary>
    public PageOptions Replace(string key, Type component)
    {
        Components[key] = component;
        return this;
    }

    public PageOptions Replace<TComponent>(string key) where TComponent : Microsoft.AspNetCore.Components.IComponent =>
        Replace(key, typeof(TComponent));

    /// <summary>Sends links and old addresses of the page to an own page of the app, e.g. "/people/{Id}".</summary>
    public PageOptions Replace(string key, string href)
    {
        Hrefs[key] = href;
        return this;
    }

    /// <summary>Removes the page: its navigation entry goes and its address finds nothing.</summary>
    public PageOptions Disable(string key)
    {
        Disabled.Add(key);
        return this;
    }
}
