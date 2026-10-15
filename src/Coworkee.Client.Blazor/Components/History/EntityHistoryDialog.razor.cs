using Coworkee.Client.Blazor.Components.Data;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components;

/// <summary>The history of a record, or without <see cref="Id"/> the deleted records of the set, as a side sheet.</summary>
public partial class EntityHistoryDialog
{
    [Parameter] public string EntitySet { get; set; } = string.Empty;

    [Parameter] public Guid? Id { get; set; }

    [Parameter] public EventCallback OnRestored { get; set; }

    public static Task ShowAsync(IDialogService dialogs, string title, string entitySet, Guid? id, EventCallback onRestored) =>
        dialogs.ShowSideSheetAsync<EntityHistoryDialog>(title, new DialogParameters<EntityHistoryDialog>
        {
            { d => d.EntitySet, entitySet },
            { d => d.Id, id },
            { d => d.OnRestored, onRestored },
        });
}
