namespace Coworkee.Cli;

/// <summary>The Coworkee solution around a directory: the folder with the .slnx file, named like the app.</summary>
internal sealed record Solution(string Root, string Name)
{
    public string Src => Path.Combine(Root, "src");

    public static Solution Find(string directory)
    {
        for (var current = new DirectoryInfo(directory); current is not null; current = current.Parent)
        {
            if (current.GetFiles("*.slnx").FirstOrDefault() is { } slnx)
            {
                return new Solution(current.FullName, Path.GetFileNameWithoutExtension(slnx.Name));
            }
        }

        throw new CliException($"No .slnx file in {directory} or above. Run this inside a Coworkee solution.");
    }

    public string SlnxPath => Path.Combine(Root, Name + ".slnx");

    public string Project(string suffix) =>
        Directory.EnumerateFiles(Src, $"*.{suffix}.csproj", SearchOption.AllDirectories).FirstOrDefault()
        ?? throw new CliException($"No *.{suffix}.csproj below {Src}.");
}
