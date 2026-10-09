using Coworkee.Client.Blazor.Localization;
using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Components;

/// <summary>Text box with a send button for a new comment, a reply or an edit.</summary>
public partial class CommentComposer
{
    private string? _text;
    private bool _sending;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    /// <summary>The text the box starts with, e.g. the comment being edited.</summary>
    [Parameter] public string? Text { get; set; }

    [Parameter] public string? Placeholder { get; set; }

    /// <summary>Returns true when the text was saved, which empties the box.</summary>
    [Parameter] public Func<string, Task<bool>>? OnSubmit { get; set; }

    [Parameter] public EventCallback OnCancel { get; set; }

    protected override void OnInitialized() => _text = Text;

    private async Task SubmitAsync()
    {
        if (OnSubmit is null || string.IsNullOrWhiteSpace(_text))
        {
            return;
        }

        _sending = true;
        if (await OnSubmit(_text))
        {
            _text = null;
        }

        _sending = false;
    }
}
