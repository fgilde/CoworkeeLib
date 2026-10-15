using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Components.Data;
using Coworkee.Client.Blazor.History;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.Auditing;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using MudBlazor.Extensions;

namespace Coworkee.Client.Blazor.Components;

/// <summary>Shows what a restore would change and asks before it runs.</summary>
public partial class EntityHistoryRestoreDialog
{
    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [CascadingParameter] private IMudDialogInstance Dialog { get; set; } = null!;

    [Parameter] public EntityHistoryPreviewDto Preview { get; set; } = new(false, []);

    [Parameter] public Func<string, string> Label { get; set; } = p => p;

    /// <summary>Previews, confirms and restores; true when the record changed.</summary>
    public static async Task<bool> ConfirmAsync(IDialogService dialogs, IEntityHistoryApi api, ISnackbar snackbar, CoworkeeLocalizer l,
        string entitySet, Guid id, EntityHistoryRestoreRequest request, Func<string, string> label)
    {
        EntityHistoryPreviewDto preview;
        try
        {
            preview = await api.PreviewAsync(entitySet, id, request);
        }
        catch (ApiException exception)
        {
            snackbar.Add(SnackbarApiExtensions.Describe(exception), Severity.Error);
            return false;
        }

        if (!preview.Undeletes && preview.Fields.Count == 0)
        {
            snackbar.Add(l["The record already has these values."], Severity.Info);
            return false;
        }

        var reference = await dialogs.ShowExAsync<EntityHistoryRestoreDialog>(l["Restore"], new DialogParameters<EntityHistoryRestoreDialog>
        {
            { d => d.Preview, preview },
            { d => d.Label, label },
        }, EditDialogExtensions.Small());
        if (await reference.Result is not { Canceled: false })
        {
            return false;
        }

        return await snackbar.RunAsync(() => api.RestoreAsync(entitySet, id, request), l["Restored."]);
    }
}
