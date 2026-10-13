using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components.Editors;

/// <summary>A list of free-text values as chips; Enter, a separator, a paste, a picked suggestion or leaving the field adds them.</summary>
public partial class ChipListEditor
{
    private static readonly char[] Separators = [' ', ',', '\n', '\r', '\t'];

    private MudAutocomplete<string>? _input;
    private string _text = string.Empty;
    private string? _error;
    private int _suggested;

    [Parameter] public IReadOnlyList<string> Value { get; set; } = [];

    [Parameter] public EventCallback<IReadOnlyList<string>> ValueChanged { get; set; }

    [Parameter] public string? Label { get; set; }

    [Parameter] public string? HelperText { get; set; }

    [Parameter] public string? Placeholder { get; set; }

    [Parameter] public bool ReadOnly { get; set; }

    /// <summary>Gets the typed text and returns the matching suggestions; values that are already chips are not offered.</summary>
    [Parameter] public Func<string, CancellationToken, Task<IEnumerable<string>>>? Suggestions { get; set; }

    /// <summary>Returns the error text of an invalid value, null for a valid one.</summary>
    [Parameter] public Func<string, string?>? Validate { get; set; }

    /// <summary>Values whose chip cannot be removed.</summary>
    [Parameter] public IReadOnlyCollection<string>? Fixed { get; set; }

    private EventCallback<MudChip<string>> CloseOf(string item) =>
        ReadOnly || Fixed?.Contains(item) == true ? default : EventCallback.Factory.Create<MudChip<string>>(this, () => RemoveAsync(item));

    private Color ChipColor(string item) => Validate?.Invoke(item) is null ? Color.Default : Color.Error;

    private async Task<IEnumerable<string>> SearchAsync(string? text, CancellationToken token) =>
        Suggestions is null ? [] : (await Suggestions(text ?? string.Empty, token)).Where(s => !Value.Contains(s));

    private async Task TypeAsync(string? text)
    {
        _text = text ?? string.Empty;
        _error = null;
        if (_text.IndexOfAny(Separators) >= 0)
        {
            await CommitAsync();
        }
    }

    private async Task PickAsync(string? value)
    {
        if (value is null)
        {
            return;
        }

        _text = value;
        await CommitAsync();
        if (_text.Length == 0 && _input is not null)
        {
            await _input.ClearAsync();
        }
    }

    // a blur with the suggestion list open is the click on a suggestion, so the text is taken when the list closes
    private Task BlurAsync(FocusEventArgs _) => _input is { Open: true } && _suggested > 0 ? Task.CompletedTask : CommitAsync();

    private Task OpenChangedAsync(bool open) => open ? Task.CompletedTask : CommitAsync();

    private Task OnEnterAsync(KeyboardEventArgs args) => args.Key is "Enter" or "NumpadEnter" ? CommitAsync() : Task.CompletedTask;

    // on key down: the key up of Escape already closes the list, and closing the list commits the text
    private async Task OnEscapeAsync(KeyboardEventArgs args)
    {
        if (args.Key == "Escape" && _input is not null)
        {
            _text = string.Empty;
            _error = null;
            await _input.ClearAsync();
        }
    }

    private async Task CommitAsync()
    {
        var tokens = _text.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
        {
            return;
        }

        var added = new List<string>();
        var rejected = new List<string>();
        _error = null;
        foreach (var token in tokens)
        {
            var error = Validate?.Invoke(token);
            if (error is not null)
            {
                if (!rejected.Contains(token))
                {
                    rejected.Add(token);
                }

                _error ??= error;
            }
            else if (!Value.Contains(token) && !added.Contains(token))
            {
                added.Add(token);
            }
        }

        _text = string.Join(' ', rejected);
        if (added.Count > 0)
        {
            await SetAsync([.. Value, .. added]);
        }
    }

    private Task RemoveAsync(string item) => SetAsync([.. Value.Where(v => v != item)]);

    private async Task SetAsync(IReadOnlyList<string> values)
    {
        Value = values;
        await ValueChanged.InvokeAsync(values);
    }
}
