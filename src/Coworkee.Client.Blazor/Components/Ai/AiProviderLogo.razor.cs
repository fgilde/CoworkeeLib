using Coworkee.Contracts.Ai;
using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Components.Ai;

/// <summary>The provider's mark in white on its brand color, or its first letter when the catalog has no mark.</summary>
public partial class AiProviderLogo
{
    [Parameter, EditorRequired] public AiProviderInfo Provider { get; set; } = null!;

    [Parameter] public int Size { get; set; } = 36;
}
