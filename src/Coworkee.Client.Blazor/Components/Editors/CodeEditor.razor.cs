using BlazorMonaco;
using BlazorMonaco.Editor;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Client.Blazor.Theming;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using MudBlazor;
using MudBlazor.Extensions;
using MudBlazor.Extensions.Options;

namespace Coworkee.Client.Blazor.Components.Editors;

/// <summary>
/// A Monaco code editor. Monaco is loaded on demand; until then, and when it cannot be loaded, the value is shown read-only.
/// Dark mode is read when Monaco creates the editor; language and read-only state follow later changes.
/// </summary>
public partial class CodeEditor : IAsyncDisposable
{
    private const int DebounceMilliseconds = 300;

    private IJSObjectReference? _module;
    private StandaloneCodeEditor? _editor;
    private CancellationTokenSource? _debounce;
    private bool _ready;
    private bool _initialized;
    private bool _dark;
    private string _content = string.Empty;
    private (string Language, string Trigger, string ModelUri)? _registered;
    private string? _modelUri;
    private List<string> _highlights = [];
    private string _language = string.Empty;
    private bool _readOnly;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Inject] private IJSRuntime JS { get; set; } = null!;

    [Inject] private IDialogService Dialogs { get; set; } = null!;

    [Inject] private ThemeService ThemeService { get; set; } = null!;

    [Parameter] public string? Value { get; set; }

    [Parameter] public EventCallback<string?> ValueChanged { get; set; }

    [Parameter] public string Language { get; set; } = "plaintext";

    [Parameter] public string? Label { get; set; }

    [Parameter] public bool ReadOnly { get; set; }

    [Parameter] public string Height { get; set; } = "320px";

    [Parameter] public IReadOnlyList<CodeCompletion>? Completions { get; set; }

    /// <summary>Text that opens the suggestions, e.g. <c>{{</c>; Monaco reacts to its last character, Ctrl+Space opens them anywhere.</summary>
    [Parameter] public string? CompletionTrigger { get; set; }

    /// <summary>A regular expression whose matches are highlighted, e.g. placeholders.</summary>
    [Parameter] public string? Highlight { get; set; }

    [Parameter] public bool AllowFullscreen { get; set; } = true;

    /// <summary>Inserts at the cursor; without Monaco it is appended to the value.</summary>
    public async Task InsertAsync(string text)
    {
        if (_editor is null || !_initialized)
        {
            await SetFromEditorAsync((Value ?? string.Empty) + text);
            return;
        }

        var selection = await _editor.GetSelection();
        await _editor.ExecuteEdits("coworkee", [new IdentifiedSingleEditOperation { Range = selection, Text = text }], (List<Selection>?)null);
        await _editor.Focus();
    }

    internal async Task SetFromEditorAsync(string? value)
    {
        Value = value;
        await ValueChanged.InvokeAsync(value);
    }

    protected override async Task OnParametersSetAsync()
    {
        var value = Value ?? string.Empty;
        if (value != _content)
        {
            _content = value;
            if (_initialized)
            {
                await _editor!.SetValue(value);
            }
        }

        if (_initialized)
        {
            await ApplyAsync();
            await RegisterCompletionsAsync();
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        try
        {
            _module = await JS.InvokeAsync<IJSObjectReference>("import", "./_content/Coworkee.Client.Blazor/coworkee.js");
            if (await _module.InvokeAsync<bool>("loadMonaco"))
            {
                _dark = ThemeService.Mode switch
                {
                    "dark" => true,
                    "light" => false,
                    _ => await _module.InvokeAsync<bool>("prefersDark"),
                };
                _ready = true;
                StateHasChanged();
            }
        }
        catch (Exception e) when (e is JSException or JSDisconnectedException)
        {
        }
    }

    private StandaloneEditorConstructionOptions Options(StandaloneCodeEditor _)
    {
        _language = Language;
        _readOnly = ReadOnly;
        return new()
        {
            Language = Language,
            Value = Value ?? string.Empty,
            ReadOnly = ReadOnly,
            AutomaticLayout = true,
            Minimap = new EditorMinimapOptions { Enabled = false },
            Theme = _dark ? "vs-dark" : "vs",
        };
    }

    internal async Task InitAsync()
    {
        _initialized = true;
        await ApplyAsync();
        await RegisterCompletionsAsync();
        await HighlightAsync(Value ?? string.Empty);
    }

    private async Task HighlightAsync(string text)
    {
        if (string.IsNullOrEmpty(Highlight) || _editor is null)
        {
            return;
        }

        try
        {
            var decorations = CodeHighlights.Of(text, Highlight).Select(range => new ModelDeltaDecoration { Range = range, Options = new ModelDecorationOptions { InlineClassName = "cw-code-highlight" } });
            _highlights = await (await _editor.GetModel()).DeltaDecorations(_highlights, [.. decorations], null);
        }
        catch (Exception e) when (e is JSException or JSDisconnectedException or System.Text.RegularExpressions.RegexMatchTimeoutException)
        {
        }
    }

    private async Task ApplyAsync()
    {
        try
        {
            if (Language != _language)
            {
                _language = Language;
                await BlazorMonaco.Editor.Global.SetModelLanguage(JS, await _editor!.GetModel(), Language);
            }

            if (ReadOnly != _readOnly)
            {
                _readOnly = ReadOnly;
                await _editor!.UpdateOptions(new EditorUpdateOptions { ReadOnly = ReadOnly });
            }
        }
        catch (Exception e) when (e is JSException or JSDisconnectedException)
        {
        }
    }

    private async Task RegisterCompletionsAsync()
    {
        if (Completions is null || string.IsNullOrEmpty(CompletionTrigger) || _editor is null)
        {
            return;
        }

        try
        {
            _modelUri ??= (await _editor.GetModel()).Uri;
            var registration = (Language, CompletionTrigger, _modelUri);
            if (registration == _registered)
            {
                return;
            }

            if (_registered is { } previous)
            {
                CodeCompletionRegistry.Remove(JS, previous.Language, previous.Trigger, previous.ModelUri);
                _registered = null;
            }

            await CodeCompletionRegistry.AddAsync(JS, registration.Language, registration.Item2, registration.Item3, () => Completions);
            _registered = registration;
        }
        catch (Exception e) when (e is JSException or JSDisconnectedException)
        {
        }
    }

    private async Task ChangedAsync(ModelContentChangedEvent _)
    {
        _debounce?.Cancel();
        _debounce?.Dispose();
        var debounce = _debounce = new CancellationTokenSource();
        try
        {
            await Task.Delay(DebounceMilliseconds, debounce.Token);
            var text = await _editor!.GetValue();
            await HighlightAsync(text);
            if (text != _content)
            {
                _content = text;
                await SetFromEditorAsync(text);
            }
        }
        catch (Exception e) when (e is OperationCanceledException or JSException or JSDisconnectedException)
        {
        }
    }

    private Task OpenFullscreenAsync() => Dialogs.ShowExAsync<CodeEditorDialog>(
        Label ?? string.Empty,
        new DialogParameters<CodeEditorDialog>
        {
            { d => d.Value, Value },
            { d => d.ValueChanged, EventCallback.Factory.Create<string?>(this, SetFromEditorAsync) },
            { d => d.Language, Language },
            { d => d.ReadOnly, ReadOnly },
            { d => d.Completions, Completions },
            { d => d.CompletionTrigger, CompletionTrigger },
            { d => d.Highlight, Highlight },
        },
        new DialogOptionsEx { FullScreen = true, CloseButton = true });

    public async ValueTask DisposeAsync()
    {
        _debounce?.Cancel();
        _debounce?.Dispose();
        if (_registered is { } registration)
        {
            CodeCompletionRegistry.Remove(JS, registration.Language, registration.Trigger, registration.ModelUri);
        }

        try
        {
            if (_module is not null)
            {
                await _module.DisposeAsync();
            }
        }
        catch (JSDisconnectedException)
        {
        }
    }
}
