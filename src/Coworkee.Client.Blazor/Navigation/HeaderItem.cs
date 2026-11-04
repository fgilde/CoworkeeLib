namespace Coworkee.Client.Blazor.Navigation;

/// <summary>An item of the app bar: one of the library's (<see cref="App"/> empty) or an app's.</summary>
public sealed record HeaderItem(string Id, string Title, string Icon, int Order, AppBarItem? App = null);
