using Coworkee.Client.Blazor.Components.Editors;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.Mailing;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using MudExRichTextEditor.Extensibility;

namespace Coworkee.Client.Blazor.Components.Mail;

/// <summary>Edits a mail body visually (placeholders as <c>{</c> mentions) or as source; the value is always plain Scriban/HTML.</summary>
public partial class MailBodyEditor : IAsyncDisposable
{
    private const int MaxSuggestions = 20;

    private enum EditorMode
    {
        Visual,
        Source,
    }

    private readonly object[] _modules;
    private EditorMode _mode;
    private bool _modeChosen;
    private string _html = string.Empty;
    private string? _seen;
    private IReadOnlyList<string> _known = [];
    private IReadOnlyList<CodeCompletion> _completions = [];
    private IReadOnlyList<MailVariableDto>? _variables;
    private RichTextEditor? _visual;
    private CodeEditor? _source;
    private ElementReference _visualHost;
    private IJSObjectReference? _module;
    private bool _widened;

    public MailBodyEditor() => _modules = [new MentionModule<MailPlaceholder>(SearchAsync, '{')];

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Inject] private IJSRuntime JS { get; set; } = null!;

    [Parameter] public string Value { get; set; } = string.Empty;

    [Parameter] public EventCallback<string> ValueChanged { get; set; }

    [Parameter] public IReadOnlyList<MailVariableDto> Variables { get; set; } = [];

    /// <summary>Height of the editor and the placeholder list, any CSS length.</summary>
    [Parameter] public string Height { get; set; } = "360px";

    private bool VisualAllowed => MailVisualMode.Allowed(Value);

    protected override void OnParametersSet()
    {
        if (!ReferenceEquals(Variables, _variables))
        {
            _variables = Variables;
            _known = [.. Variables.Where(v => !v.IsList && !v.IsItem()).Select(v => v.Name)];
            _completions =
            [
                .. Variables.Where(v => !v.IsList).Select(v => new CodeCompletion(v.Name, $" {v.Expression()} }}}}", v.Type)),
                // shortcut: the cursor lands after the closing braces, not inside the statement
                new("if", " if  }}"),
                new("else", " else }}"),
                new("end", " end }}"),
                new("for", " for item in  }}"),
            ];
        }

        if (!_modeChosen)
        {
            _modeChosen = true;
            _mode = VisualAllowed ? EditorMode.Visual : EditorMode.Source;
        }

        if (Value != _seen)
        {
            _seen = Value;
            _html = MailMentionHtml.ToMentions(Value, _known);
            if (_mode == EditorMode.Visual && !VisualAllowed)
            {
                _mode = EditorMode.Source;
            }
        }
    }

    public async Task InsertVariableAsync(MailVariableDto variable)
    {
        var snippet = variable.IsList ? $"{{{{ for item in {variable.Name} }}}}\n{{{{ end }}}}" : $"{{{{ {variable.Expression()} }}}}";
        if (_mode == EditorMode.Source && _source is not null)
        {
            await _source.InsertAsync(snippet);
        }
        else if (variable.IsList || variable.IsItem() || _visual is null)
        {
            // loops and their items cannot be built visually, so they go to the end of the source
            _mode = EditorMode.Source;
            await EmitAsync(Value + snippet);
        }
        else
        {
            await _visual.InsertHtmlAsync(MailMentionHtml.ToMentions(snippet, _known));
        }
    }

    private Task<IEnumerable<MailPlaceholder>> SearchAsync(char denotation, string term) => Task.FromResult(
        _known.Where(name => name.Contains(term, StringComparison.OrdinalIgnoreCase)).Take(MaxSuggestions).Select(name => new MailPlaceholder(name)));

    private Task SetModeAsync(EditorMode mode)
    {
        if (mode == EditorMode.Visual)
        {
            if (!VisualAllowed)
            {
                return Task.CompletedTask;
            }

            _html = MailMentionHtml.ToMentions(Value, _known);
        }

        _mode = mode;
        return Task.CompletedTask;
    }

    private Task VisualChangedAsync(string html)
    {
        _html = html;
        return EmitAsync(MailMentionHtml.ToScriban(html, _known));
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        // TipTap takes any character up to the next space; only Quill's mention list needs widening
        if (_mode != EditorMode.Visual || _visual?.Backend != RichTextBackend.Quill)
        {
            _widened = false;
            return;
        }

        if (_widened)
        {
            return;
        }

        _widened = true;
        try
        {
            _module ??= await JS.InvokeAsync<IJSObjectReference>("import", "./_content/Coworkee.Client.Blazor/coworkee.js");
            await _module.InvokeVoidAsync("widenMentionChars", _visualHost);
        }
        catch (Exception e) when (e is JSException or JSDisconnectedException)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
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

    private Task SourceChangedAsync(string? value) => EmitAsync(value ?? string.Empty);

    private async Task EmitAsync(string value)
    {
        _seen = Value = value;
        await ValueChanged.InvokeAsync(value);
    }
}
