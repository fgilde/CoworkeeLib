using MudBlazor;

namespace Coworkee.Client.Blazor.Components.Data.Import;

public static class ImportWizardExtensions
{
    /// <summary>Opens the import wizard of an entity set as side sheet; true when rows were written.</summary>
    public static async Task<bool> ShowImportWizardAsync(this IDialogService dialogs, string title, string entitySet)
    {
        var parameters = new DialogParameters<ImportWizard> { { w => w.EntitySet, entitySet } };
        var dialog = await dialogs.ShowSideSheetAsync<ImportWizard>(title, parameters, o => o.MaxWidth = MaxWidth.Large);
        return await dialog.Result is { Canceled: false };
    }
}
