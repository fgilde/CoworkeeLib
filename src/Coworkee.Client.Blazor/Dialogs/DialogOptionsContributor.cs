using MudBlazor;
using MudBlazor.Extensions;
using MudBlazor.Extensions.Options;

namespace Coworkee.Client.Blazor.Dialogs;

/// <summary>
/// The options of every dialog the library opens, per <see cref="DialogPart"/>. An app derives from it, overrides the parts it wants different
/// and registers it: <c>builder.Services.AddSingleton&lt;DialogOptionsContributor, MyDialogOptions&gt;();</c>.
/// Each method returns a new instance; callers may change it.
/// </summary>
public class DialogOptionsContributor
{
    /// <summary>Whether administrators may change the parts under Administration, User interface, Dialog options (<see cref="Contracts.Settings.DialogSettings.Options"/>).</summary>
    public virtual bool AdminEditable => false;

    /// <summary>Edit forms and detail sheets; the library opens them on the side the user clicked on.</summary>
    public virtual DialogOptionsEx SideSheet(bool clickedLeft) => new()
    {
        CloseButton = true,
        CloseOnEscapeKey = true,
        MaxWidth = MaxWidth.Medium,
        FullWidth = true,
        BackdropClick = false,
        MaximizeButton = true,
        DragMode = MudDialogDragMode.Simple,
        Position = clickedLeft ? DialogPosition.CenterLeft : DialogPosition.CenterRight,
        Animations = [AnimationType.SlideIn],
        FullHeight = true,
        DisableSizeMarginY = true,
        DisablePositionMargin = true,
    };

    /// <summary>A narrow full height sheet that always slides in from the right, like the about dialog.</summary>
    public virtual DialogOptionsEx RightSheet() => new()
    {
        CloseButton = true,
        CloseOnEscapeKey = true,
        MaxWidth = MaxWidth.Small,
        FullWidth = true,
        Position = DialogPosition.CenterRight,
        Animations = [AnimationType.SlideIn],
        FullHeight = true,
        DisableSizeMarginY = true,
        DisablePositionMargin = true,
    };

    /// <summary>Small centered dialogs such as confirmations.</summary>
    public virtual DialogOptionsEx Small() => new()
    {
        CloseButton = true,
        CloseOnEscapeKey = true,
        MaxWidth = MaxWidth.Small,
        FullWidth = true,
        BackdropClick = false,
        Animations = [AnimationType.FadeIn, AnimationType.FlipX],
    };

    /// <summary>Editors opened over the whole screen, like the code editor.</summary>
    public virtual DialogOptionsEx Fullscreen() => new() { FullScreen = true, CloseButton = true };

    /// <summary>Progress that cannot be closed until it ends, like an export.</summary>
    public virtual DialogOptionsEx Progress() => new()
    {
        MaxWidth = MaxWidth.ExtraSmall,
        FullWidth = true,
        BackdropClick = false,
        CloseOnEscapeKey = false,
        CloseButton = false,
    };

    /// <summary>An item of a list edited in a settings form.</summary>
    public virtual DialogOptionsEx ListItem() => new()
    {
        MaxWidth = MaxWidth.Medium,
        FullWidth = true,
        CloseButton = true,
        Resizeable = true,
        DragMode = MudDialogDragMode.Simple,
    };

    /// <summary>The options of <paramref name="part"/>; <paramref name="clickedLeft"/> matters only for the side sheet.</summary>
    public DialogOptionsEx Of(DialogPart part, bool clickedLeft = false) => part switch
    {
        DialogPart.SideSheet => SideSheet(clickedLeft),
        DialogPart.RightSheet => RightSheet(),
        DialogPart.Small => Small(),
        DialogPart.Fullscreen => Fullscreen(),
        DialogPart.Progress => Progress(),
        DialogPart.ListItem => ListItem(),
        _ => throw new ArgumentOutOfRangeException(nameof(part), part, null),
    };
}
