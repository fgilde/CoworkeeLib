using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.Theming;
using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Pages.Admin;

/// <summary>The theme's <see cref="SignInDesign"/>; edits <see cref="Value"/> in place.</summary>
public partial class ThemeSignInEdit
{
    [Inject] private CoworkeeClientOptions Options { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Parameter, EditorRequired] public SignInDesign Value { get; set; } = null!;
}
