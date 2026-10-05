using Coworkee.Client.Blazor.Api;
using Coworkee.Contracts.Settings;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components;

public partial class SettingsForm
{
    [Inject] private ICoworkeeApi Api { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    private IReadOnlyList<SettingGroupDto>? _groups;
    private Dictionary<string, SettingValueDto> _stored = [];
    private readonly Dictionary<string, string?> _edited = new(StringComparer.Ordinal);
    private bool _saving;

    [Parameter, EditorRequired] public SettingScope Scope { get; set; }

    /// <summary>Settings the page offers in its own way (e.g. the theme as cards).</summary>
    [Parameter] public IReadOnlySet<string>? Hide { get; set; }

    protected override async Task OnParametersSetAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        var definitions = await Api.GetSettingDefinitionsAsync(Scope == SettingScope.User);
        _stored = (await Api.GetSettingsAsync(Scope)).ToDictionary(v => v.Name, StringComparer.Ordinal);
        _groups = definitions
            .Select(g => g with { Settings = g.Settings.Where(s => s.Scopes.Contains(Scope) && Hide?.Contains(s.Name) != true).ToList() })
            .Where(g => g.Settings.Count > 0)
            .ToList();
        _edited.Clear();
    }

    private string? Current(SettingDefinitionDto setting) =>
        _edited.TryGetValue(setting.Name, out var edited) ? edited : _stored.GetValueOrDefault(setting.Name)?.Value;

    private bool IsStored(SettingDefinitionDto setting) => _stored.GetValueOrDefault(setting.Name)?.HasValue == true;

    private void Set(SettingDefinitionDto setting, string? value)
    {
        var normalized = string.IsNullOrEmpty(value) ? null : value;
        if (setting.Type == SettingType.Secret ? normalized is null : normalized == _stored.GetValueOrDefault(setting.Name)?.Value)
        {
            _edited.Remove(setting.Name);
        }
        else
        {
            _edited[setting.Name] = normalized;
        }
    }

    private async Task SaveAsync()
    {
        _saving = true;
        try
        {
            await Api.SetSettingsAsync(Scope, new Dictionary<string, string?>(_edited, StringComparer.Ordinal));
            Snackbar.Add("Settings saved.", Severity.Success);
            await LoadAsync();
        }
        catch (ApiException exception)
        {
            Snackbar.Add(exception.Errors is { Count: > 0 } errors ? string.Join(" ", errors.SelectMany(e => e.Value)) : exception.Message, Severity.Error);
        }
        finally
        {
            _saving = false;
        }
    }
}
