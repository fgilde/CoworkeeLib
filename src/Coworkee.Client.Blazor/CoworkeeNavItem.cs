namespace Coworkee.Client.Blazor;

/// <summary>
/// A link of the navigation; hidden without <paramref name="Permission"/>, while the bool <paramref name="Feature"/> is off or when <paramref name="Visible"/> answers false
/// (asked whenever the menu builds, after sign-in and permission changes: keep it cheap). <paramref name="HostOnly"/> ones show only to users of the system organisation.
/// </summary>
public sealed record CoworkeeNavItem(
    string Title, string Href, string Icon, string? Permission = null, bool ForceLoad = false, string? Group = null, int Order = 0, string? Feature = null, bool HostOnly = false,
    Func<IServiceProvider, Task<bool>>? Visible = null);
