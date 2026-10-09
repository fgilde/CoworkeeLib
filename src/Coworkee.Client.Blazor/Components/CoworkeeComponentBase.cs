using Coworkee.Client.Blazor.Theming;
using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Components;

/// <summary>Base of the library's components (set in _Imports.razor): gives them the theme's <see cref="Theming.Density"/>.</summary>
public abstract class CoworkeeComponentBase : ComponentBase
{
    [CascadingParameter] protected Density Density { get; set; } = Density.Default;
}
