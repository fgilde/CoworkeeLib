namespace Coworkee.Client.Blazor.Navigation;

/// <summary>Counts the navigation shows next to its links, by address, e.g. the unread chat messages on "/chat".</summary>
public sealed class NavigationBadges
{
    private readonly Dictionary<string, int> _counts = new(StringComparer.OrdinalIgnoreCase);

    public event Action? Changed;

    public int this[string? href] => href is not null && _counts.TryGetValue(href, out var count) ? count : 0;

    public void Set(string href, int count)
    {
        if (this[href] != count)
        {
            _counts[href] = count;
            Changed?.Invoke();
        }
    }
}
