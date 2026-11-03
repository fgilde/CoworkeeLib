namespace Coworkee.Contracts.Settings;

/// <summary>The engine of every rich text editor of the library: <see cref="TipTap"/> (default) or <see cref="Quill"/>.</summary>
public static class EditorSettings
{
    public const string RichTextEditor = "Editors.RichTextEditor";

    public const string TipTap = "TipTap";

    public const string Quill = "Quill";
}
