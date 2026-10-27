using Coworkee.Client.Blazor.Theming;
using Coworkee.Contracts.Settings;
using Microsoft.AspNetCore.Components;
using MudExRichTextEditor;
using MudExRichTextEditor.Extensibility;

namespace Coworkee.Client.Blazor.Components.Editors;

/// <summary>
/// The rich text editor of the library: TipTap or Quill as the admin chose in <c>Editors.RichTextEditor</c>.
/// <see cref="Modules"/> takes modules of either engine (<see cref="MentionModule{T}"/> fits both); each engine gets the ones it understands.
/// </summary>
public partial class RichTextEditor
{
    private MudExRichTextEdit? _quill;
    private MudExTiptapEdit? _tiptap;
    private IQuillModule[] _quillModules = [];
    private ITiptapModule[] _tiptapModules = [];

    [Inject] private IServiceProvider Services { get; set; } = null!;

    [Parameter] public string? Value { get; set; }

    [Parameter] public EventCallback<string> ValueChanged { get; set; }

    [Parameter] public RichTextBackend Engine { get; set; }

    [Parameter] public IReadOnlyList<object> Modules { get; set; } = [];

    /// <summary>Any CSS length; the editor fills it. Without one it grows with its text.</summary>
    [Parameter] public string? Height { get; set; }

    [Parameter] public bool Immediate { get; set; } = true;

    [Parameter] public bool ReadOnly { get; set; }

    /// <summary>The toolbar; null keeps the editor's default.</summary>
    [Parameter] public MudExRichTextEditor.Types.QuillTool[]? Tools { get; set; }

    /// <summary>Shown while empty; null keeps the editor's own (translated) text.</summary>
    [Parameter] public string? Placeholder { get; set; }

    private const string DefaultPlaceholder = "Insert text here...";

    /// <summary>The engine in use: <see cref="Engine"/>, else the setting, else TipTap.</summary>
    public RichTextBackend Backend => Engine != RichTextBackend.FromSettings ? Engine
        : string.Equals((Services.GetService(typeof(ThemeService)) as ThemeService)?.ClientSettings.GetValueOrDefault(EditorSettings.RichTextEditor), EditorSettings.Quill, StringComparison.OrdinalIgnoreCase)
            ? RichTextBackend.Quill : RichTextBackend.TipTap;

    /// <summary>The rendered editor, whichever engine it is.</summary>
    public IMudExRichTextEdit? Editor => (IMudExRichTextEdit?)_quill ?? _tiptap;

    protected override void OnParametersSet()
    {
        _quillModules = [.. Modules.OfType<IQuillModule>()];
        _tiptapModules = [.. Modules.OfType<ITiptapModule>()];
    }

    public Task InsertHtmlAsync(string html) => Editor?.InsertHtmlAsync(html) ?? Task.CompletedTask;

    // MudExRichTextEditor 9.5.5 shows an empty toolbar for an explicit Tools="null", so the parameter is only passed when set.
    // ponytail: back to Tools="Tools" once the release with the null fix is referenced
    private IReadOnlyDictionary<string, object> ToolsParameter => Tools is null ? new Dictionary<string, object>() : new Dictionary<string, object> { [nameof(Tools)] = Tools };
}
