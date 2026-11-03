namespace Coworkee.Client.Blazor.Navigation;

public sealed record NavigationGroup(string Title, IReadOnlyList<CoworkeeNavItem> Items);
