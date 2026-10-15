using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Data;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.Data;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components.Data.Import;

/// <summary>A field per column (suggested by the server), how rows are written, and mappings saved per set for the next file.</summary>
public partial class ImportMappingStep
{
    private IReadOnlyList<ImportProfileDto> _profiles = [];
    private ImportProfileDto? _profile;
    private string? _profileName;

    [Parameter, EditorRequired] public ImportWizardState State { get; set; } = null!;

    [Parameter] public EventCallback Changed { get; set; }

    [Inject] private IDataTransferApi Api { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    protected override Task OnInitializedAsync() => LoadProfilesAsync();

    private string Label(ImportFieldDto field) =>
        field.KeyOnly ? L["Id (only to find rows)"] : field.Required ? $"{L[field.Label]} *" : L[field.Label];

    private Task Map(int index, string? field)
    {
        State.SetColumn(index, field);
        return Changed.InvokeAsync();
    }

    private Task SetMode(ImportMode mode)
    {
        State.Mode = mode;
        State.KeyField ??= State.Target.KeyFields.FirstOrDefault(State.Columns.Contains);
        State.Preview = null;
        return Changed.InvokeAsync();
    }

    private Task SetKey(string key)
    {
        State.KeyField = key;
        State.Preview = null;
        return Changed.InvokeAsync();
    }

    private void SetCulture(string culture)
    {
        State.Culture = culture;
        State.Preview = null;
    }

    private Task ApplyProfile(ImportProfileDto profile)
    {
        _profile = profile;
        _profileName = profile.Name;
        State.Apply(profile);
        return Changed.InvokeAsync();
    }

    private async Task SaveProfileAsync()
    {
        if (await Snackbar.RunAsync(() => Api.SaveProfileAsync(State.Target.EntitySet, State.Profile(_profileName!.Trim())), L["Mapping saved."]))
        {
            await LoadProfilesAsync();
            _profile = _profiles.FirstOrDefault(p => p.Name == _profileName!.Trim());
        }
    }

    private async Task DeleteProfileAsync()
    {
        if (await Snackbar.RunAsync(() => Api.DeleteProfileAsync(_profile!.Id)))
        {
            _profile = null;
            await LoadProfilesAsync();
        }
    }

    private Task LoadProfilesAsync() => Snackbar.RunAsync(async () => _profiles = await Api.GetProfilesAsync(State.Target.EntitySet));
}
