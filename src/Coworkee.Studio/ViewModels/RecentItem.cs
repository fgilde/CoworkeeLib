using Coworkee.Studio.Model;

namespace Coworkee.Studio.ViewModels;

/// <summary>A spec file or an app on the start page, newest first; opening an app shows its overview.</summary>
public sealed record RecentItem(string Path, string Name, bool IsApp, DateTimeOffset When)
{
    public string Icon => IsApp ? "Icon.Layers" : "Icon.File";

    /// <summary>The folder name and the day of the last change or opening; the tooltip has the whole path.</summary>
    public string Folder => $"{System.IO.Path.GetFileName(IsApp ? System.IO.Path.TrimEndingDirectorySeparator(Path) : System.IO.Path.GetDirectoryName(Path))} · {When.LocalDateTime.ToString("d", System.Globalization.CultureInfo.CurrentCulture)}";

    public static IReadOnlyList<RecentItem> Of(StudioSettings settings, int count) =>
    [
        .. settings.RecentSpecs.Where(File.Exists).Select(f => new RecentItem(f, System.IO.Path.GetFileNameWithoutExtension(f), false, File.GetLastWriteTime(f)))
            .Concat(settings.Apps.Where(a => Directory.Exists(a.Path)).Select(a => new RecentItem(a.Path, a.Name, true, a.Opened ?? a.Created)))
            .OrderByDescending(r => r.When)
            .Take(count),
    ];
}
