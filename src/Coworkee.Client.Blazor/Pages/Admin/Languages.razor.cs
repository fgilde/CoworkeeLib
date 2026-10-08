using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Components.Data;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.Localization;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Pages.Admin;

public partial class Languages
{
    private static readonly string[] SearchFields = [nameof(LanguageDto.Name), nameof(LanguageDto.Culture)];
    private CoworkeeDataTable<LanguageDto> _table = null!;

    [Inject] private ILocalizationApi Api { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Inject] private IDialogService Dialogs { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    private Task CreateAsync() => EditAsync(null, new AddEditLanguageRequest());

    private Task EditAsync(LanguageDto language) =>
        EditAsync(language.Id, new AddEditLanguageRequest { Culture = language.Culture, Name = language.Name, IsEnabled = language.IsEnabled, IsDefault = language.IsDefault });

    private async Task EditAsync(Guid? id, AddEditLanguageRequest model)
    {
        if (await Dialogs.ShowEditAsync(L[id is null ? "New language" : "Edit language"], model) is { } saved)
        {
            await Snackbar.RunAsync(() => Api.SaveLanguageAsync(id, saved), L["Saved"]);
        }
    }

    private Task DeleteAsync(IReadOnlyCollection<LanguageDto> languages) =>
        Snackbar.RunAsync(() => Api.DeleteLanguagesAsync([.. languages.Where(l => l.Id is not null).Select(l => l.Id!.Value)]));
}
