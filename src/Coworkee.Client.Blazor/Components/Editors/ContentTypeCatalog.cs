using MudBlazor;

namespace Coworkee.Client.Blazor.Components.Editors;

public sealed record ContentTypeInfo(string Value, string Name, string Icon);

public sealed record ContentTypeGroup(string Name, string Icon, IReadOnlyList<string> Values);

/// <summary>Common MIME types with a readable name; <see cref="Groups"/> add several at once.</summary>
public static class ContentTypeCatalog
{
    private const string Image = Icons.Material.Outlined.Image;
    private const string Pdf = Icons.Material.Outlined.PictureAsPdf;
    private const string Document = Icons.Material.Outlined.Description;
    private const string Table = Icons.Material.Outlined.TableChart;
    private const string Slides = Icons.Material.Outlined.Slideshow;
    private const string Video = Icons.Material.Outlined.Movie;
    private const string Audio = Icons.Material.Outlined.AudioFile;
    private const string Archive = Icons.Material.Outlined.FolderZip;
    private const string File = Icons.Material.Outlined.InsertDriveFile;

    public static IReadOnlyList<ContentTypeInfo> Known { get; } =
    [
        new("image/*", "All images", Image),
        new("image/jpeg", "JPEG image", Image),
        new("image/png", "PNG image", Image),
        new("image/gif", "GIF image", Image),
        new("image/webp", "WebP image", Image),
        new("image/svg+xml", "SVG image", Image),
        new("image/heic", "HEIC image", Image),
        new("application/pdf", "PDF", Pdf),
        new("application/msword", "Word (.doc)", Document),
        new("application/vnd.openxmlformats-officedocument.wordprocessingml.document", "Word (.docx)", Document),
        new("application/vnd.ms-excel", "Excel (.xls)", Table),
        new("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "Excel (.xlsx)", Table),
        new("application/vnd.ms-powerpoint", "PowerPoint (.ppt)", Slides),
        new("application/vnd.openxmlformats-officedocument.presentationml.presentation", "PowerPoint (.pptx)", Slides),
        new("application/vnd.oasis.opendocument.text", "OpenDocument text (.odt)", Document),
        new("application/vnd.oasis.opendocument.spreadsheet", "OpenDocument spreadsheet (.ods)", Table),
        new("text/plain", "Text (.txt)", Document),
        new("text/csv", "CSV", Table),
        new("video/*", "All videos", Video),
        new("video/mp4", "MP4 video", Video),
        new("video/webm", "WebM video", Video),
        new("video/quicktime", "QuickTime video", Video),
        new("audio/*", "All audio", Audio),
        new("audio/mpeg", "MP3 audio", Audio),
        new("audio/wav", "WAV audio", Audio),
        new("audio/ogg", "Ogg audio", Audio),
        new("application/zip", "ZIP archive", Archive),
        new("application/x-7z-compressed", "7-Zip archive", Archive),
        new("application/gzip", "GZip archive", Archive),
    ];

    public static IReadOnlyList<ContentTypeGroup> Groups { get; } =
    [
        new("Images", Image, ["image/*"]),
        new("PDF", Pdf, ["application/pdf"]),
        new("Office documents", Document, [.. Known.Where(t => t.Value.StartsWith("application/", StringComparison.Ordinal) && t.Icon is Document or Table or Slides).Select(t => t.Value)]),
        new("Videos", Video, ["video/*"]),
        new("Audio", Audio, ["audio/*"]),
        new("Archives", Archive, [.. Known.Where(t => t.Icon == Archive).Select(t => t.Value)]),
    ];

    /// <summary>The known entry, else the value itself with the icon of its kind ("image/x-icon" gets the image icon).</summary>
    public static ContentTypeInfo Describe(string value) =>
        Known.FirstOrDefault(t => string.Equals(t.Value, value, StringComparison.OrdinalIgnoreCase))
        ?? new(value, value, Known.FirstOrDefault(t => t.Value == value.Split('/')[0] + "/*")?.Icon ?? File);

    /// <summary>"type/subtype" or "type/*" like the upload compares them.</summary>
    public static bool IsValid(string? value) => value is not null && System.Text.RegularExpressions.Regex.IsMatch(value, @"^[\w.+-]+/([\w.+-]+|\*)$");
}
