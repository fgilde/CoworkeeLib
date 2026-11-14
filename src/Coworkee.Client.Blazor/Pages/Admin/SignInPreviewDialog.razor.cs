using Coworkee.Client.Blazor.Localization;
using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Pages.Admin;

/// <summary>
/// The auth server's sign-in page with an unsaved design in a frame: /bff/sign-in-preview sends the frame to the auth server's /Account/Preview,
/// which renders the real page with the design and answers only inside a frame of a registered client's origin.
/// </summary>
public partial class SignInPreviewDialog
{
    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    /// <summary>The design as JSON, like the app configuration section stores it.</summary>
    [Parameter, EditorRequired] public string Design { get; set; } = string.Empty;

    private string Src => "/bff/sign-in-preview?design=" + Uri.EscapeDataString(Design);
}
