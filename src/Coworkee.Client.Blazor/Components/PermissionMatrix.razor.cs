using Coworkee.Client.Blazor.Api;
using Coworkee.Contracts.Identity;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components;

public partial class PermissionMatrix
{
    [Inject] private ICoworkeeApi Api { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    private IReadOnlyList<PermissionGroupDto>? _groups;
    private readonly HashSet<string> _selected = new(StringComparer.Ordinal);
    private bool _saving;
    private string? _search;

    private bool Matches(PermissionDto permission) =>
        string.IsNullOrWhiteSpace(_search)
        || permission.DisplayName.Contains(_search.Trim(), StringComparison.OrdinalIgnoreCase)
        || permission.Name.Contains(_search.Trim(), StringComparison.OrdinalIgnoreCase);

    private IEnumerable<PermissionGroupDto> Visible() =>
        _groups!.Where(g => g.Permissions.Any(Matches));

    private bool Known(string name) => _groups!.Any(g => g.Permissions.Any(p => p.Name == name));

    private void ToggleGroup(PermissionGroupDto group, bool on)
    {
        foreach (var permission in group.Permissions)
        {
            Toggle(permission.Name, on);
        }
    }

    [Parameter, EditorRequired] public PermissionProviderType ProviderType { get; set; }

    [Parameter, EditorRequired] public Guid ProviderKey { get; set; }

    [Parameter] public bool ReadOnly { get; set; }

    protected override async Task OnParametersSetAsync()
    {
        var definitions = Api.GetPermissionDefinitionsAsync();
        var grants = ReadOnly ? Task.FromResult<IReadOnlyList<string>>([]) : Api.GetGrantsAsync(ProviderType, ProviderKey);
        _groups = await definitions;
        _selected.Clear();
        _selected.UnionWith(await grants);
    }

    private void Toggle(string name, bool on)
    {
        if (on)
        {
            _selected.Add(name);
        }
        else
        {
            _selected.Remove(name);
        }
    }

    private async Task SaveAsync()
    {
        _saving = true;
        try
        {
            var ordered = _groups!.SelectMany(g => g.Permissions).Select(p => p.Name).Where(_selected.Contains).ToList();
            await Api.SetGrantsAsync(ProviderType, ProviderKey, ordered);
            Snackbar.Add("Permissions saved.", Severity.Success);
        }
        catch (ApiException exception)
        {
            Snackbar.Add(exception.Message, Severity.Error);
        }
        finally
        {
            _saving = false;
        }
    }
}
