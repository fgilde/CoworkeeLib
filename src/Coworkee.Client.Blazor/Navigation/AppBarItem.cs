namespace Coworkee.Client.Blazor.Navigation;

/// <summary>
/// A component of the app bar. <paramref name="Id"/> is its key in the administrator's header configuration (the component's type name when empty),
/// <paramref name="Title"/> its name there; <paramref name="Order"/> places it among the other app items, all between the search and the language menu.
/// </summary>
public sealed record AppBarItem(Type Component, int Order = 0, IDictionary<string, object>? Parameters = null, string? Id = null, string? Title = null)
{
    public string Id { get; init; } = string.IsNullOrWhiteSpace(Id) ? Component.Name : Id;

    public string Title { get; init; } = Title ?? (string.IsNullOrWhiteSpace(Id) ? Component.Name : Id);
}
