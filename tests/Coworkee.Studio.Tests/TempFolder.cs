namespace Coworkee.Studio.Tests;

internal sealed class TempFolder : IDisposable
{
    public string Path { get; } = Directory.CreateDirectory(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "coworkee-studio-tests", Guid.NewGuid().ToString("n"))).FullName;

    public string File(string name) => System.IO.Path.Combine(Path, name);

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
