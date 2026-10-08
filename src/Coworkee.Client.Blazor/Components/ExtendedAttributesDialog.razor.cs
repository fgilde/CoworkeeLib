using Coworkee.Client.Blazor.Localization;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components;

public partial class ExtendedAttributesDialog
{
    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [CascadingParameter] private IMudDialogInstance Dialog { get; set; } = null!;

    [Parameter] public string Title { get; set; } = string.Empty;

    [Parameter] public string EntityType { get; set; } = string.Empty;

    [Parameter] public Guid EntityId { get; set; }

    [Parameter] public bool ReadOnly { get; set; }

    public static Task ShowAsync(IDialogService dialogs, string title, string entityType, Guid entityId, bool readOnly = false) =>
        dialogs.ShowAsync<ExtendedAttributesDialog>(null, new DialogParameters<ExtendedAttributesDialog>
        {
            { d => d.Title, title },
            { d => d.EntityType, entityType },
            { d => d.EntityId, entityId },
            { d => d.ReadOnly, readOnly },
        }, new DialogOptions { MaxWidth = MaxWidth.Medium, FullWidth = true });
}
