using System.Text.Json;

namespace Coworkee.Cli.Spec;

/// <summary>The starter specs that travel inside the CLI and the Studio.</summary>
public static class SpecGallery
{
    private static readonly Lazy<IReadOnlyList<GalleryEntry>> Entries = new(() => JsonSerializer.Deserialize<List<GalleryEntry>>(Text("gallery"), SpecJson.Options)!);

    public static IReadOnlyList<GalleryEntry> All => Entries.Value;

    public static GalleryEntry? Find(string id) => All.FirstOrDefault(e => e.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    /// <summary>The spec of <paramref name="id"/> as it is in the repository.</summary>
    public static string Json(string id) => Text(Find(id)?.Id ?? throw new KeyNotFoundException($"No starter spec '{id}'; known are {string.Join(", ", All.Select(e => e.Id))}."));

    public static AppSpec Read(string id) => SpecJson.Read(Json(id));

    private static string Text(string name)
    {
        using var stream = typeof(SpecGallery).Assembly.GetManifestResourceStream($"Specs.{name}.json") ?? throw new KeyNotFoundException(name);
        return new StreamReader(stream).ReadToEnd();
    }
}
