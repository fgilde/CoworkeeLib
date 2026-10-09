using Coworkee.Client.Blazor.Api;
using MudBlazor;
using MudBlazor.Extensions;
using MudBlazor.Extensions.Components.ObjectEdit;
using MudBlazor.Extensions.Components.ObjectEdit.Options;
using MudBlazor.Extensions.Core;
using MudBlazor.Extensions.Options;

namespace Coworkee.Client.Blazor.Components.Data;

/// <summary>Edit forms as full height side sheets that slide in from the side the user clicked on.</summary>
public static class EditDialogExtensions
{
    /// <summary>Asks for the values only; the caller saves them.</summary>
    public static async Task<TModel?> ShowEditAsync<TModel>(this IDialogService dialogs, string title, TModel model, Action<ObjectEditMeta<TModel>>? meta = null)
    {
        var (cancelled, result) = await dialogs.EditObjectAsync(model, title, await SideSheetAsync(), null, Parameters(model, meta));
        return cancelled ? default : result;
    }

    /// <summary>Saves inside the dialog: it stays open with the messages of the API when saving fails.</summary>
    public static async Task<bool> ShowEditAsync<TModel>(this IDialogService dialogs, string title, TModel model, Func<TModel, Task> save, Action<ObjectEditMeta<TModel>>? meta = null)
    {
        var (cancelled, _) = await dialogs.EditObjectAsync(model, title, (value, _) => SaveAsync(save, value), await SideSheetAsync(), null, Parameters(model, meta));
        return !cancelled;
    }

    /// <summary>Shows any dialog component as side sheet.</summary>
    public static async Task<IMudExDialogReference<TDialog>> ShowSideSheetAsync<TDialog>(this IDialogService dialogs, string title, DialogParameters parameters, Action<DialogOptionsEx>? configure = null)
        where TDialog : Microsoft.AspNetCore.Components.ComponentBase
    {
        var options = await SideSheetAsync();
        configure?.Invoke(options);
        return await dialogs.ShowExAsync<TDialog>(title, parameters, options);
    }

    public static async Task<DialogOptionsEx> SideSheetAsync() => new()
    {
        CloseButton = true,
        CloseOnEscapeKey = true,
        MaxWidth = MaxWidth.Medium,
        FullWidth = true,
        BackdropClick = false,
        MaximizeButton = true,
        DragMode = MudDialogDragMode.Simple,
        Position = await DialogPlacement.ClickedLeftAsync() ? DialogPosition.CenterLeft : DialogPosition.CenterRight,
        Animations = [AnimationType.SlideIn],
        FullHeight = true,
        DisableSizeMarginY = true,
        DisablePositionMargin = true,
    };

    /// <summary>Asks before something that cannot be undone.</summary>
    public static Task<bool> ConfirmAsync(this IDialogService dialogs, string title, string message, string confirmText, string cancelText, string? icon = null) =>
        dialogs.ShowConfirmationDialogAsync(title, message, confirmText, cancelText, icon ?? Icons.Material.Outlined.HelpOutline, Small());

    /// <summary>Small centered dialogs such as confirmations.</summary>
    public static DialogOptionsEx Small() => new()
    {
        CloseButton = true,
        CloseOnEscapeKey = true,
        MaxWidth = MaxWidth.Small,
        FullWidth = true,
        BackdropClick = false,
        Animations = [AnimationType.FadeIn, AnimationType.FlipX],
    };

    // two columns on wider screens like the classic forms; a page's meta can widen single fields with WrapInMudItem(i => i.md = 12)
#pragma warning disable BL0005 // MudEx configures the wrapping grid items through these instances
    private static Action<ObjectEditMeta<TModel>> Grid<TModel>(Action<ObjectEditMeta<TModel>>? meta) => m =>
    {
        m.WrapEachInMudItem(i =>
        {
            i.xs = 12;
            i.md = 6;
        });
        meta?.Invoke(m);
    };
#pragma warning restore BL0005

    // the meta is configured before the dialog renders: MudEx applies a MetaConfiguration only after its editors took their labels
    private static DialogParameters Parameters<TModel>(TModel model, Action<ObjectEditMeta<TModel>>? meta) => new()
    {
        { nameof(MudExObjectEditDialog<TModel>.DialogIcon), Icon(model) },
        { nameof(MudExObjectEditDialog<TModel>.MetaInformation), model.ObjectEditMeta(Grid(meta)) },
        // so the registered configurations (the theme's density) still apply to the prepared meta
        { nameof(MudExObjectEditDialog<TModel>.ConfigureMetaInformationAlways), true },
    };

    private static string Icon<TModel>(TModel model) =>
        model?.GetType().GetProperty("Id")?.GetValue(model) is { } id && !Equals(id, Guid.Empty) ? Icons.Material.Filled.Edit : Icons.Material.Filled.Add;

    private static async Task<string?> SaveAsync<TModel>(Func<TModel, Task> save, TModel value)
    {
        try
        {
            await save(value);
            return null;
        }
        catch (ApiException exception)
        {
            return exception.Errors is { Count: > 0 } errors ? string.Join(Environment.NewLine, errors.SelectMany(e => e.Value)) : exception.Message;
        }
    }
}
