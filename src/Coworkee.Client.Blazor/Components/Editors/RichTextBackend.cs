namespace Coworkee.Client.Blazor.Components.Editors;

/// <summary>The engine of <see cref="RichTextEditor"/>; <see cref="FromSettings"/> follows the setting <c>Editors.RichTextEditor</c>.</summary>
public enum RichTextBackend
{
    FromSettings,
    TipTap,
    Quill,
}
