namespace Coworkee.Studio.ViewModels;

public sealed record RecentSpec(string Path)
{
    public string Name => System.IO.Path.GetFileNameWithoutExtension(Path);

    /// <summary>The folder name and the day of the last change; the tooltip has the whole path.</summary>
    public string Folder => $"{System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(Path))} · {File.GetLastWriteTime(Path).ToString("d", System.Globalization.CultureInfo.CurrentCulture)}";
}
