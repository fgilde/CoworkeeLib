using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.Ai;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components.Ai;

/// <summary>Latency, answer and tool support of a connection test, or its error.</summary>
public partial class AiTestResult
{
    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Parameter, EditorRequired] public AiConnectionTestDto Result { get; set; } = null!;

    private Severity Severity => Result switch
    {
        { Succeeded: false } => Severity.Error,
        { CallsTools: false } => Severity.Warning,
        _ => Severity.Success,
    };
}
