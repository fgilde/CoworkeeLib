using Coworkee.Client.Blazor.Theming;
using Coworkee.Contracts.Settings;
using Microsoft.AspNetCore.Components;
using MudBlazor.Extensions.Core;
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

    [Parameter] public MudExSize<double>? Height { get; set; }

    [Parameter] public bool Immediate { get; set; } = true;

    [Parameter] public bool ReadOnly { get; set; }

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
}
