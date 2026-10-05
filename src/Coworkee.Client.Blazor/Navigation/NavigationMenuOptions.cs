namespace Coworkee.Client.Blazor.Navigation;

public sealed class NavigationMenuOptions
{
    private readonly HashSet<string> _hidden = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _groupOrder = new(StringComparer.Ordinal);

    public bool ShowHome { get; set; } = true;

    public string HomeTitle { get; set; } = "Home";

    public IReadOnlySet<string> Hidden => _hidden;

    public NavigationMenuOptions Hide(params string[] hrefs)
    {
        _hidden.UnionWith(hrefs);
        return this;
    }

    public NavigationMenuOptions OrderGroup(string group, int order)
    {
        _groupOrder[group] = order;
        return this;
    }

    public int GroupOrder(string group) => _groupOrder.GetValueOrDefault(group, 1000);
}
