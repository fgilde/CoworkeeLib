using MudBlazor;
using MudBlazor.Extensions.Components.ObjectEdit.Options;

namespace Coworkee.Client.Blazor.Components.Data;

public static class EditDialogExtensions
{
    public static async Task<TModel?> ShowEditAsync<TModel>(this IDialogService dialogs, string title, TModel model, Action<ObjectEditMeta<TModel>>? meta = null)
    {
        var parameters = new DialogParameters<CoworkeeEditDialog<TModel>>
        {
            { d => d.Title, title },
            { d => d.Model, model },
            { d => d.Meta, meta },
        };
        var dialog = await dialogs.ShowAsync<CoworkeeEditDialog<TModel>>(title, parameters, new DialogOptions { MaxWidth = MaxWidth.Medium, FullWidth = true, CloseOnEscapeKey = true });
        var result = await dialog.Result;
        return result is { Canceled: false, Data: TModel saved } ? saved : default;
    }
}
