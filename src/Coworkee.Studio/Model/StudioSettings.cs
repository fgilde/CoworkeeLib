using System.Text.Json;

namespace Coworkee.Studio.Model;

/// <summary>Per user, in the application data folder: recent specs, how to start the CLI, template source, package feed, language and theme.</summary>
public sealed class StudioSettings
{
    private const int MaxRecent = 8;

    public static string DefaultFile { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Coworkee", "Studio", "settings.json");

    public List<string> RecentSpecs { get; set; } = [];

    /// <summary>coworkee executable or Coworkee.Cli.dll; empty: the global tool.</summary>
    public string? CliPath { get; set; }

    /// <summary>.nupkg or folder of Coworkee.Templates for coworkee new --template-source.</summary>
    public string? TemplateSource { get; set; }

    /// <summary>Folder or address of extra packages (local packs) the new app restores from.</summary>
    public string? PackageFeed { get; set; }

    /// <summary>Folder in which the last app was created.</summary>
    public string? Location { get; set; }

    /// <summary>en, de or empty for the language of the system.</summary>
    public string? Language { get; set; }

    /// <summary>Light, Dark or empty to follow the system.</summary>
    public string? Theme { get; set; }

    public static StudioSettings Load(string? file = null)
    {
        try
        {
            return JsonSerializer.Deserialize<StudioSettings>(File.ReadAllText(file ?? DefaultFile)) ?? new();
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return new();
        }
    }

    public void Save(string? file = null)
    {
        file ??= DefaultFile;
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }

    public void AddRecent(string specFile)
    {
        RecentSpecs.RemoveAll(f => string.Equals(f, specFile, StringComparison.OrdinalIgnoreCase));
        RecentSpecs.Insert(0, specFile);
        RecentSpecs = [.. RecentSpecs.Take(MaxRecent)];
    }
}
