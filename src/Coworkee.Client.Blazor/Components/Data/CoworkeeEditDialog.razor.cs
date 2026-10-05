using Microsoft.AspNetCore.Components;
using MudBlazor;
using MudBlazor.Extensions.Components.ObjectEdit.Options;

namespace Coworkee.Client.Blazor.Components.Data;

public partial class CoworkeeEditDialog<TModel>
{
    [CascadingParameter] private IMudDialogInstance Dialog { get; set; } = null!;

    [Parameter] public string Title { get; set; } = string.Empty;

    [Parameter, EditorRequired] public TModel Model { get; set; } = default!;

    [Parameter] public Action<ObjectEditMeta<TModel>>? Meta { get; set; }

    private void Save() => Dialog.Close(DialogResult.Ok(Model));

    private void Cancel() => Dialog.Cancel();
}
