[assembly: AssemblyFixture(typeof(Coworkee.Cli.Tests.TemplateFixture))]

namespace Coworkee.Cli.Tests;

/// <summary>Packs the template from source with a known version and installs it into a private hive, not the user's.</summary>
public sealed class TemplateFixture : IAsyncLifetime
{
    private readonly TempFolder _folder = new();

    public string Version { get; private set; } = "1.2.3";

    public string Root => _folder.Path;

    private string Hive => Path.Combine(Root, "hive");

    public async ValueTask InitializeAsync()
    {
        if (Environment.GetEnvironmentVariable("COWORKEE_TEST_FEED") is { Length: > 0 } feed)
        {
            Version = Path.GetFileNameWithoutExtension(Directory.GetFiles(feed, "Coworkee.Core.*.nupkg").OrderBy(File.GetLastWriteTimeUtc).Last())["Coworkee.Core.".Length..];
        }

        var project = Path.Combine(Dotnet.RepositoryRoot(), "templates", "Coworkee.Templates", "Coworkee.Templates.csproj");
        var packages = Path.Combine(Root, "packages");
        await Dotnet.RunAsync(Root, "pack", project, "-o", packages, $"-p:Version={Version}",
            $"-p:BaseIntermediateOutputPath={Path.Combine(Root, "obj")}{Path.DirectorySeparatorChar}", $"-p:BaseOutputPath={Path.Combine(Root, "bin")}{Path.DirectorySeparatorChar}");
        await Dotnet.RunAsync(Root, "new", "install", Directory.GetFiles(packages, "*.nupkg").Single(), "--debug:custom-hive", Hive);
    }

    public async Task<string> CreateAsync(string folder, params string[] options)
    {
        var output = Path.Combine(Root, folder);
        await Dotnet.RunAsync(Root, ["new", "coworkee", "-n", "Shop", "-o", output, "--debug:custom-hive", Hive, .. options]);
        return output;
    }

    public ValueTask DisposeAsync()
    {
        _folder.Dispose();
        return ValueTask.CompletedTask;
    }
}
