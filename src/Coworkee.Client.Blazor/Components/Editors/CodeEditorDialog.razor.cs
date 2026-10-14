using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Components.Editors;

/// <summary>The full screen view of a <see cref="CodeEditor"/>: edits a copy and hands every change back to the editor it came from.</summary>
public partial class CodeEditorDialog
{
    private string? _value;

    [Parameter] public string? Value { get; set; }

    [Parameter] public EventCallback<string?> ValueChanged { get; set; }

    [Parameter] public string Language { get; set; } = "plaintext";

    [Parameter] public bool ReadOnly { get; set; }

    [Parameter] public IReadOnlyList<CodeCompletion>? Completions { get; set; }

    [Parameter] public string? CompletionTrigger { get; set; }

    protected override void OnInitialized() => _value = Value;

    private async Task PushAsync(string? value)
    {
        _value = value;
        await ValueChanged.InvokeAsync(value);
    }
}
