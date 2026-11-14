using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Client.Blazor.Theming;
using Coworkee.Contracts.Settings;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using MudBlazor.Extensions.Components.ObjectEdit;
using MudBlazor.Extensions.Components.ObjectEdit.Options;

namespace Coworkee.Client.Blazor.Components;

/// <summary>The settings of a scope in a MudExObjectEdit form; every defined setting of the scope shows up, grouped like its definition.</summary>
public partial class SettingsForm
{

    [Inject] private ICoworkeeApi Api { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Inject] private ThemeService ThemeService { get; set; } = null!;

    private SettingsModel? _model;
    private ObjectEditMeta<object>? _meta;
    private bool _saving;
    private int _revision;
    private SettingScope? _loadedScope;

    [Parameter, EditorRequired] public SettingScope Scope { get; set; }

    /// <summary>Settings the page offers in its own way (e.g. the theme as cards).</summary>
    [Parameter] public IReadOnlySet<string>? Hide { get; set; }

    /// <summary>Only the settings of these groups (e.g. "Security" and "Account" on the page Sign-in), also groups with a page of their own; empty shows all others.</summary>
    [Parameter] public IReadOnlySet<string>? Groups { get; set; }

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
            .Where(g => Groups is { Count: > 0 } only ? only.Contains(g.Name) : g.Page is null)
            .Select(g => g with { Settings = g.Settings.Where(s => s.Scopes.Contains(Scope) && Hide?.Contains(s.Name) != true).ToList() })
            .Where(g => g.Settings.Count > 0)
            .ToList();
        _model = new SettingsModel(groups, stored, Scope);
        Rebuild();
    }

    // someone saved a setting of this scope: show what applies now, unless the form holds changes of its own
    private async Task ChangedAsync()
    {
        if (!_saving && _model?.Changes() is not { Count: > 0 })
        {
            await LoadAsync();
        }
    }

    private void Rebuild()
    {
        _meta = _model!.Value.ObjectEditMeta(meta => _model.Configure(meta, L, Scope));
        _revision++;
    }

    // MudEx swaps its value for the parsed file; the model keeps the edited object
    private void Imported(ImportedData<object> imported)
    {
        _model?.Import(imported.Json);
        Rebuild();
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
            await ThemeService.LoadAsync();
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
