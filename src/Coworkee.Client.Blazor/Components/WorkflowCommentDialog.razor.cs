using Coworkee.Client.Blazor.Localization;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components;

/// <summary>Asks for the comment of a transition or a decision; closes with the text, empty when it was optional and left out.</summary>
public partial class WorkflowCommentDialog
{
    private string? _comment;

    [CascadingParameter] private IMudDialogInstance Dialog { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Parameter] public bool Required { get; set; }
}
