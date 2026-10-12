using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Client.Blazor.Security;
using Coworkee.Contracts.Identity;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components;

/// <summary>Creates a personal API key: a name, an expiry and the permissions, picked from the user's own. Closes with the <see cref="CreatedApiKeyDto"/>.</summary>
public partial class ApiKeyDialog
{
    private static readonly int[] Lifetimes = [7, 30, 90, 180, 365];

    private readonly HashSet<string> _selected = new(StringComparer.Ordinal);
    private IReadOnlyList<PermissionGroupDto>? _groups;
    private string? _name;
    private string? _description;
    private int _days = 90;
    private bool _busy;

    [CascadingParameter] private IMudDialogInstance Dialog { get; set; } = null!;

    [Inject] private IApiKeysApi Api { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    protected override Task OnInitializedAsync() => Snackbar.RunAsync(async () => _groups = await Api.GetScopesAsync());

    private void Toggle(string permission, bool on)
    {
        if (on)
        {
            _selected.Add(permission);
        }
        else
        {
            _selected.Remove(permission);
        }
    }

    private void ToggleGroup(PermissionGroupDto group, bool on)
    {
        foreach (var permission in group.Permissions)
        {
            Toggle(permission.Name, on);
        }
    }

    private void Cancel() => Dialog.Cancel();

    private async Task CreateAsync()
    {
        _busy = true;
        CreatedApiKeyDto? created = null;
        var scopes = _groups!.SelectMany(g => g.Permissions).Select(p => p.Name).Where(_selected.Contains).ToList();
        await Snackbar.RunAsync(async () => created = await Api.CreateAsync(new CreateApiKeyRequest(_name!.Trim(), _description, DateTimeOffset.UtcNow.AddDays(_days), scopes)));
        _busy = false;
        if (created is not null)
        {
            Dialog.Close(DialogResult.Ok(created));
        }
    }
}
