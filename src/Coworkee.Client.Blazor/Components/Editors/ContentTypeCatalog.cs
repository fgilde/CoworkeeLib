using MudBlazor;
using Nextended.Core;

namespace Coworkee.Client.Blazor.Components.Editors;

/// <param name="Extensions">The file extensions of the type (".jpg, .jpeg"); searchable and shown next to the value.</param>
public sealed record ContentTypeInfo(string Value, string Name, string Icon, string Group, string Extensions = "");

public sealed record ContentTypeGroup(string Name, string Icon, IReadOnlyList<string> Values);

/// <summary>
/// Every MIME type Nextended's <see cref="MimeType"/> knows, grouped by kind with an icon; per group the wildcard ("image/*")
/// and the common types come first with a readable name. <see cref="Groups"/> add several at once.
/// </summary>
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
    private const string Font = Icons.Material.Outlined.FontDownload;
    private const string Model = Icons.Material.Outlined.ViewInAr;
    private const string File = Icons.Material.Outlined.InsertDriveFile;

    private static readonly string[] Kinds = ["Images", "Documents", "Videos", "Audio", "Archives", "Text", "Fonts", "3D models", "Other"];

    private static readonly (string Value, string Name)[] Common =
    [
        ("image/*", "All images"),
        ("image/jpeg", "JPEG image"),
        ("image/png", "PNG image"),
        ("image/gif", "GIF image"),
        ("image/webp", "WebP image"),
        ("image/svg+xml", "SVG image"),
        ("image/heic", "HEIC image"),
        ("application/pdf", "PDF"),
        ("application/msword", "Word (.doc)"),
        ("application/vnd.openxmlformats-officedocument.wordprocessingml.document", "Word (.docx)"),
        ("application/vnd.ms-excel", "Excel (.xls)"),
        ("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "Excel (.xlsx)"),
        ("application/vnd.ms-powerpoint", "PowerPoint (.ppt)"),
        ("application/vnd.openxmlformats-officedocument.presentationml.presentation", "PowerPoint (.pptx)"),
        ("application/vnd.oasis.opendocument.text", "OpenDocument text (.odt)"),
        ("application/vnd.oasis.opendocument.spreadsheet", "OpenDocument spreadsheet (.ods)"),
        ("text/*", "All text files"),
        ("text/plain", "Text (.txt)"),
        ("text/csv", "CSV"),
        ("video/*", "All videos"),
        ("video/mp4", "MP4 video"),
        ("video/webm", "WebM video"),
        ("video/quicktime", "QuickTime video"),
        ("audio/*", "All audio"),
        ("audio/mpeg", "MP3 audio"),
        ("audio/wav", "WAV audio"),
        ("audio/ogg", "Ogg audio"),
        ("application/zip", "ZIP archive"),
        ("application/x-7z-compressed", "7-Zip archive"),
        ("application/gzip", "GZip archive"),
    ];

    // MimeType.IsArchive rebuilds its archive list with regular expressions on every call: once for the catalog, not once per type
    private static readonly Lazy<HashSet<string>> Archives = new(() => new(MimeType.ArchiveTypes, StringComparer.OrdinalIgnoreCase));

    private static readonly Lazy<IReadOnlyList<ContentTypeInfo>> All = new(Build);

    private static readonly Lazy<Dictionary<string, ContentTypeInfo>> ByValue = new(() => All.Value.ToDictionary(t => t.Value, StringComparer.OrdinalIgnoreCase));

    /// <summary>Every known type by kind; within a kind the common ones first, then the rest by value.</summary>
    public static IReadOnlyList<ContentTypeInfo> Known => All.Value;

    public static IReadOnlyList<ContentTypeGroup> Groups { get; } =
    [
        new("Images", Image, ["image/*"]),
        new("PDF", Pdf, ["application/pdf"]),
        new("Office documents", Document, [.. Common.Where(t => t.Value.StartsWith("application/", StringComparison.Ordinal) && Kind(t.Value).Icon is Document or Table or Slides).Select(t => t.Value)]),
        new("Videos", Video, ["video/*"]),
        new("Audio", Audio, ["audio/*"]),
        new("Archives", Archive, [.. Common.Where(t => Kind(t.Value).Icon == Archive).Select(t => t.Value)]),
    ];

    /// <summary>The known entry, else the value itself with the icon of its kind ("image/x-icon" gets the image icon).</summary>
    public static ContentTypeInfo Describe(string value) =>
        ByValue.Value.GetValueOrDefault(value) ?? Info(value, value, string.Empty);

    private static List<ContentTypeInfo> Build()
    {
        var extensions = MimeType.All
            .GroupBy(e => e.Value.ToLowerInvariant())
            .ToDictionary(g => g.Key, g => string.Join(", ", g.Select(e => "." + e.Key.ToLowerInvariant()).Order(StringComparer.Ordinal)));
        var common = Common.Select(t => Info(t.Value, t.Name, extensions.GetValueOrDefault(t.Value, string.Empty)));
        var rest = extensions
            .Where(e => !Common.Any(t => t.Value == e.Key))
            .OrderBy(e => e.Key, StringComparer.Ordinal)
            .Select(e => Info(e.Key, e.Key, e.Value));
        return [.. common.Concat(rest).OrderBy(t => Array.IndexOf(Kinds, t.Group))]; // OrderBy is stable: the common ones stay first
    }

    private static ContentTypeInfo Info(string value, string name, string extensions)
    {
        var (kind, icon) = Kind(value.ToLowerInvariant());
        return new(value, name, icon, kind, extensions);
    }

    private static (string Kind, string Icon) Kind(string value) => value.Split('/')[0] switch
    {
        "image" => ("Images", Image),
        "video" => ("Videos", Video),
        "audio" => ("Audio", Audio),
        "font" => ("Fonts", Font),
        "model" => ("3D models", Model),
        _ when value == "application/pdf" => ("Documents", Pdf),
        _ when value == "application/gzip" || Archives.Value.Contains(value) => ("Archives", Archive),
        _ when value == "text/csv" || value.Contains("excel") || value.Contains("spreadsheet") => ("Documents", Table),
        _ when value.Contains("powerpoint") || value.Contains("presentation") => ("Documents", Slides),
        _ when value is "application/msword" or "application/rtf" || value.Contains("wordprocessing") || value.Contains("opendocument") => ("Documents", Document),
        "text" => ("Text", Document),
        _ when value.Contains("font") => ("Fonts", Font),
        _ => ("Other", File),
    };
}
