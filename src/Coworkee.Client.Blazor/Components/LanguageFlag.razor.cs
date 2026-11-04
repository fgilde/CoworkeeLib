using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Components;

/// <summary>The flag of a culture's country ("de-AT" → AT, "en" → GB unless CoworkeeClientOptions.LanguageRegions says otherwise).</summary>
public partial class LanguageFlag
{
    [Parameter, EditorRequired] public string Culture { get; set; } = string.Empty;

    /// <summary>Height in pixels; the flag is 4:3.</summary>
    [Parameter] public int Size { get; set; } = 16;

    [Parameter] public string? Alt { get; set; }

    [Parameter] public string? Style { get; set; }

    [Inject] private CoworkeeClientOptions Options { get; set; } = null!;
}
