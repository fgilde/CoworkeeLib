using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Components;
using Coworkee.Client.Blazor.Components.Data;
using Coworkee.Client.Blazor.Features;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.Features;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Pages.Admin;

public partial class Editions
{
    private IReadOnlyList<EditionDto>? _editions;
    private IReadOnlyList<FeatureGroupDto> _groups = [];

    [Inject] private IFeaturesApi Api { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Inject] private IDialogService Dialogs { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    protected override async Task OnInitializedAsync()
    {
        await Snackbar.RunAsync(async () => _groups = await Api.GetDefinitionsAsync());
        await LoadAsync();
    }

    private async Task LoadAsync() => await Snackbar.RunAsync(async () => _editions = await Api.GetEditionsAsync());

    private string FeatureName(string name) =>
        _groups.SelectMany(g => g.Features).FirstOrDefault(f => f.Name == name) is { } feature ? L[feature.DisplayName] : name;

    private async Task CreateAsync()
    {
        if (await Dialogs.ShowEditAsync(L["New edition"], new EditionForm()) is { } form
            && await Snackbar.RunAsync(() => Api.CreateEditionAsync(new EditionRequest(form.Name, form.Description, null)), L["Edition created"]))
        {
            await LoadAsync();
        }
    }

    private async Task EditAsync(EditionDto edition)
    {
        var form = new EditionForm { Name = edition.Name, Description = edition.Description };
        if (await Dialogs.ShowEditAsync(L["Edit edition"], form, f => Api.UpdateEditionAsync(edition.Id, new EditionRequest(f.Name, f.Description, edition.Values))))
        {
            await LoadAsync();
        }
    }

    private async Task EditValuesAsync(EditionDto edition)
    {
        if (await FeatureValuesDialog.ShowAsync(Dialogs, L["Features of {0}", edition.Name], _groups, edition.Values, FeatureValuesDialog.Defaults(_groups)) is { } values
            && await Snackbar.RunAsync(() => Api.UpdateEditionAsync(edition.Id, new EditionRequest(edition.Name, edition.Description, values)), L["Features saved"]))
        {
            await LoadAsync();
        }
    }

    private async Task DeleteAsync(EditionDto edition)
    {
        if (await Dialogs.ConfirmAsync(L["Delete"], L["Delete {0}? Its tenants fall back to the defaults.", edition.Name], L["Delete"], L["Cancel"], Icons.Material.Outlined.DeleteForever)
            && await Snackbar.RunAsync(() => Api.DeleteEditionAsync(edition.Id)))
        {
            await LoadAsync();
        }
    }

    private sealed class EditionForm
    {
        public string Name { get; set; } = string.Empty;

        public string? Description { get; set; }
    }
}
