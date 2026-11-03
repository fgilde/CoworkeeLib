namespace Coworkee.Contracts.Configuration;

/// <summary>An icon of the web manifest.</summary>
public sealed class BffPwaIcon
{
    /// <summary>Local path, e.g. <c>/icons/512.png</c>.</summary>
    public string Src { get; set; } = string.Empty;

    /// <summary>As in the manifest, e.g. <c>512x512</c>; default <c>any</c>.</summary>
    public string? Sizes { get; set; }

    /// <summary>The media type; default from the file extension.</summary>
    public string? Type { get; set; }

    /// <summary><c>any</c> (default), <c>maskable</c> or <c>monochrome</c>.</summary>
    public string? Purpose { get; set; }
}
