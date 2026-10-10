using Coworkee.Studio.Cli;
using Coworkee.Studio.Model;

namespace Coworkee.Studio.Tests;

/// <summary>
/// Creates a real app through the generator and the CLI of this repository with STUDIO_E2E_FEED=artifacts/nuget (build/pack-local.ps1 first).
/// It installs the packed template into the dotnet new templates of the user, like coworkee new does.
/// </summary>
public sealed class EndToEndTests
{
    private static readonly string? Feed = Environment.GetEnvironmentVariable("STUDIO_E2E_FEED");

    [Fact]
    public async Task Creates_an_app_with_the_local_cli()
    {
        Assert.SkipWhen(Feed is null, "Set STUDIO_E2E_FEED to the folder of build/pack-local.ps1.");
        var feed = Path.GetFullPath(Feed!);
        var root = RepositoryRoot();
        var cli = Directory.GetFiles(Path.Combine(root, "src", "Coworkee.Cli", "bin"), "Coworkee.Cli.dll", SearchOption.AllDirectories).OrderBy(File.GetLastWriteTimeUtc).Last();
        var template = Directory.GetFiles(feed, "Coworkee.Templates.*.nupkg").OrderBy(File.GetLastWriteTimeUtc).Last();
        using var folder = new TempFolder();
        var main = Scenario.Shop(folder);
        var lines = new List<CliLine>();

        var outcome = await new Generator(Processes.RunAsync).RunAsync(
            main.ToSpec(), new StudioSettings { CliPath = cli, TemplateSource = template, PackageFeed = feed }, lines.Add, TestContext.Current.CancellationToken);

        outcome.ShouldBe(GenerationOutcome.Succeeded, string.Join(Environment.NewLine, lines.Select(l => $"{l.Kind}: {l.Text}")));
        var app = Path.Combine(folder.Path, "Shop");
        File.Exists(Path.Combine(app, "Shop.slnx")).ShouldBeTrue();
        File.Exists(Path.Combine(app, "src", "Shop.Sales", "Domain", "Order.cs")).ShouldBeTrue();
        File.Exists(Path.Combine(app, "src", "Shop.Web.Client", "Pages", "Sales", "CustomersPage.razor")).ShouldBeTrue();
        Directory.Exists(Path.Combine(app, "src", "Shop.Catalog")).ShouldBeFalse();
        lines.Where(l => l.Kind == CliLineKind.Step).Select(l => l.Text).ShouldContain("Generating module Sales");
        lines.ShouldContain(l => l.Kind == CliLineKind.NextSteps);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Coworkee.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Coworkee.slnx not found above the test output.");
    }
}
