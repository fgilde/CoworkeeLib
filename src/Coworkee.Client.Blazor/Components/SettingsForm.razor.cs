using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.Settings;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using MudBlazor.Extensions.Components.ObjectEdit;
using MudBlazor.Extensions.Components.ObjectEdit.Options;

namespace Coworkee.Client.Blazor.Components;

/// <summary>The settings of a scope in a MudExObjectEdit form; every defined setting of the scope shows up, grouped like its definition.</summary>
public partial class SettingsForm
{
    private static readonly GlobalResetSettings NoReset = new() { AllowReset = false };
    private static readonly PropertyResetSettings NoPropertyReset = new() { AllowReset = false };

    [Inject] private ICoworkeeApi Api { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    private SettingsModel? _model;
    private ObjectEditMeta<object>? _meta;
    private bool _saving;
    private int _revision;
    private SettingScope? _loadedScope;

    [Parameter, EditorRequired] public SettingScope Scope { get; set; }

    /// <summary>Settings the page offers in its own way (e.g. the theme as cards).</summary>
    [Parameter] public IReadOnlySet<string>? Hide { get; set; }

    protected override async Task OnParametersSetAsync()
    {
        if (_loadedScope != Scope)
        {
            _loadedScope = Scope;
            await LoadAsync();
        }
    }

    private async Task LoadAsync()
    {
        var definitions = await Api.GetSettingDefinitionsAsync(Scope == SettingScope.User);
        var stored = (await Api.GetSettingsAsync(Scope)).ToDictionary(v => v.Name, StringComparer.Ordinal);
        var groups = definitions
            .Where(g => g.Page is null)
            .Select(g => g with { Settings = g.Settings.Where(s => s.Scopes.Contains(Scope) && Hide?.Contains(s.Name) != true).ToList() })
            .Where(g => g.Settings.Count > 0)
            .ToList();
        _model = new SettingsModel(groups, stored);
        _meta = _model.Value.ObjectEditMeta(meta => _model.Configure(meta, L));
        _revision++;
    }

    public async Task SaveAsync()
    {
        if (_model?.Changes() is not { Count: > 0 } changes)
        {
            return;
        }

        _saving = true;
        try
        {
            await Api.SetSettingsAsync(Scope, changes);
            Snackbar.Add(L["Settings saved."], Severity.Success);
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
