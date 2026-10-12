using Coworkee.Client.Blazor.Ai;
using Coworkee.Client.Blazor.Localization;
using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Components.Ai;

/// <summary>Temperature, token and tool limits, extra headers and the key header of a connection.</summary>
public partial class AiAdvancedOptions
{
    private const string HeadersExample = "HTTP-Referer: https://myapp.example\nX-Title: My app";

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Parameter, EditorRequired] public AiConnectionForm Form { get; set; } = null!;
}
