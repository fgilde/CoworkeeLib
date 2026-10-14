using Coworkee.Client.Blazor.Components.Editors;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.Mailing;
using Microsoft.AspNetCore.Components;
using MudExRichTextEditor;
using MudExRichTextEditor.Extensibility;

namespace Coworkee.Client.Blazor.Components.Mail;

/// <summary>Edits a mail body visually (placeholders as <c>{</c> mentions) or as source; the value is always plain Scriban/HTML.</summary>
public partial class MailBodyEditor
{
    private const int MaxSuggestions = 20;

    private enum EditorMode
    {
        Visual,
        Source,
    }

    // Quill shows the mention's ToString(), which a record DTO does not give
    private sealed record Placeholder(string Name)
    {
        public override string ToString() => Name;
    }

    private readonly IQuillModule[] _modules;
    private EditorMode _mode;
    private bool _modeChosen;
    private string _html = string.Empty;
    private string? _seen;
    private IReadOnlyList<string> _known = [];
    private IReadOnlyList<CodeCompletion> _completions = [];
    private MudExRichTextEdit? _visual;
    private CodeEditor? _source;

    public MailBodyEditor() => _modules = [new QuillMentionModule<Placeholder>(SearchAsync, '{')];

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Parameter] public string Value { get; set; } = string.Empty;

    [Parameter] public EventCallback<string> ValueChanged { get; set; }

    [Parameter] public IReadOnlyList<MailVariableDto> Variables { get; set; } = [];

    private bool VisualAllowed => MailVisualMode.Allowed(Value);

    protected override void OnParametersSet()
    {
        _known = [.. Variables.Where(v => !v.IsList && !IsItem(v)).Select(v => v.Name)];
        _completions =
        [
            .. Variables.Where(v => !v.IsList).Select(v => new CodeCompletion(v.Name, $" {Expression(v)} }}}}", v.Type)),
            // shortcut: the cursor lands after the closing braces, not inside the statement
            new("if", " if  }}"),
            new("else", " else }}"),
            new("end", " end }}"),
            new("for", " for item in  }}"),
        ];
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
        var snippet = variable.IsList ? $"{{{{ for item in {variable.Name} }}}}\n{{{{ end }}}}" : $"{{{{ {Expression(variable)} }}}}";
        if (_mode == EditorMode.Source && _source is not null)
        {
            await _source.InsertAsync(snippet);
        }
        else if (variable.IsList || IsItem(variable) || _visual is null)
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

    private static bool IsItem(MailVariableDto variable) => variable.Name.Contains("[]", StringComparison.Ordinal);

    // a member of a list element is written as a member of the loop variable of the skeleton
    private static string Expression(MailVariableDto variable) => IsItem(variable) ? "item" + variable.Name[(variable.Name.IndexOf("[]", StringComparison.Ordinal) + 2)..] : variable.Name;

    private Task<IEnumerable<Placeholder>> SearchAsync(char denotation, string term) => Task.FromResult(
        _known.Where(name => name.Contains(term, StringComparison.OrdinalIgnoreCase)).Take(MaxSuggestions).Select(name => new Placeholder(name)));

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

    private Task SourceChangedAsync(string? value) => EmitAsync(value ?? string.Empty);

    private async Task EmitAsync(string value)
    {
        _seen = Value = value;
        await ValueChanged.InvokeAsync(value);
    }
}
