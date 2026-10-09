namespace Coworkee.Cli.Tests;

internal sealed class TempFolder : IDisposable
{
    public string Path { get; } = Directory.CreateDirectory(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "coworkee-cli-" + Guid.NewGuid().ToString("N"))).FullName;

    public string File(string relative)
    {
        var path = System.IO.Path.GetFullPath(System.IO.Path.Combine(Path, relative));
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        System.IO.File.WriteAllText(path, string.Empty);
        return path;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
