namespace Coworkee.Client.Blazor.Navigation;

/// <summary>
/// A page of the library, rendered by <see cref="CoworkeePageHost"/>. <paramref name="Routes"/> are the old addresses without the
/// leading slash (e.g. "admin/users/{Id:guid}"); admin pages ("admin/…") are served under <see cref="CoworkeeClientOptions.PagePrefix"/>.
/// </summary>
public sealed record CoworkeePage(string Key, Type Component, params string[] Routes)
{
    public bool IsAdmin => Routes.All(r => r.StartsWith("admin/", StringComparison.OrdinalIgnoreCase));
}
