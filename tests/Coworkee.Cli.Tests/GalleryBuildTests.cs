using Coworkee.Cli.Commands;
using Coworkee.Cli.Spec;

namespace Coworkee.Cli.Tests;

// every starter spec creates an app that builds, workflows included
public sealed class GalleryBuildTests(TemplateFixture template)
{
    public static TheoryData<string> Starters => [.. SpecGallery.All.Select(e => e.Id)];

    [Theory]
    [MemberData(nameof(Starters))]
    public async Task A_starter_spec_creates_an_app_that_builds(string id)
    {
        var feed = Environment.GetEnvironmentVariable("COWORKEE_TEST_FEED");
        Assert.SkipWhen(string.IsNullOrEmpty(feed), "Set COWORKEE_TEST_FEED to a folder with packed Coworkee packages (build/pack-local.ps1) to build the starter specs.");
        var spec = SpecGallery.Read(id);
        spec.Name = "Shop";
        using var assets = new TempFolder();
        var folder = "gallery-" + id;
        var settings = new NewSettings(spec, assets.Path, Path.Combine(template.Root, folder), null);

        var app = await template.CreateAsync(folder, NewCommand.TemplateArgs(settings)[7..]);
        await Dotnet.RunAsync(app, "nuget", "add", "source", Path.GetFullPath(feed!), "--name", "local", "--configfile", "nuget.config");
        (await NewCommand.CompleteAsync(settings, TestContext.Current.CancellationToken)).ShouldBe(0);

        await Dotnet.RunAsync(app, "build", "Shop.slnx");
    }
}
