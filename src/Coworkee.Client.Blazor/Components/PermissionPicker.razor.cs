using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Security;
using Coworkee.Contracts.Identity;
using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Components;

/// <summary>
/// Picks permissions from the server's definitions (grouped, with display names and a search); the chosen ones show as removable chips.
/// With <see cref="ShowMatch"/> and two or more chosen it also asks whether any or all of them are needed.
/// </summary>
public partial class PermissionPicker
{
    private IReadOnlyList<PermissionGroupDto>? _groups;
    private string? _error;
    private string? _search;

    [Inject] private ICoworkeeApi Api { get; set; } = null!;

    [Inject] private Localization.CoworkeeLocalizer L { get; set; } = null!;

    /// <summary>The names of the chosen permissions.</summary>
    [Parameter] public IReadOnlyList<string> Value { get; set; } = [];

    [Parameter] public EventCallback<IReadOnlyList<string>> ValueChanged { get; set; }

    /// <summary>Asks for <see cref="Match"/> once two or more permissions are chosen.</summary>
    [Parameter] public bool ShowMatch { get; set; }

    [Parameter] public PermissionMatch Match { get; set; }

    [Parameter] public EventCallback<PermissionMatch> MatchChanged { get; set; }

    protected override async Task OnInitializedAsync()
    {
        try
        {
            _groups = await Api.GetPermissionDefinitionsAsync();
        }
        catch (ApiException exception)
        {
            _groups = [];
            _error = exception.Message;
        }
    }

    private bool Matches(PermissionDto permission) =>
        string.IsNullOrWhiteSpace(_search)
        || permission.DisplayName.Contains(_search.Trim(), StringComparison.OrdinalIgnoreCase)
        || permission.Name.Contains(_search.Trim(), StringComparison.OrdinalIgnoreCase);

    private IEnumerable<PermissionGroupDto> Visible() => _groups!.Where(g => g.Permissions.Any(Matches));

    // permissions that are gone (a deleted custom one) still show by their name, so they can be removed
    private string DisplayName(string name) =>
        _groups?.SelectMany(g => g.Permissions).FirstOrDefault(p => p.Name == name)?.DisplayName ?? name;

    private Task ToggleAsync(string name, bool on) =>
        SetValueAsync(on ? [.. Value.Where(v => v != name), name] : [.. Value.Where(v => v != name)]);

    private async Task SetValueAsync(IReadOnlyList<string> value)
    {
        Value = value;
        await ValueChanged.InvokeAsync(value);
    }

    private async Task SetMatchAsync(PermissionMatch match)
    {
        Match = match;
        await MatchChanged.InvokeAsync(match);
    }
}
