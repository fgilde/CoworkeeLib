namespace Coworkee.Cli.Tests;

public sealed class LocalFeedTests
{
    [Fact]
    public void A_local_template_with_its_packages_becomes_a_package_source_of_the_app_once()
    {
        using var feed = new TempFolder();
        using var app = new TempFolder();
        var template = feed.File("Coworkee.Templates.1.0.0-dev.nupkg");
        LocalFeed.Of(template).ShouldBeNull();
        feed.File("Coworkee.Core.1.0.0-dev.nupkg");
        var config = app.File("nuget.config");
        File.WriteAllText(config, "<?xml version=\"1.0\" encoding=\"utf-8\"?><configuration><packageSources><clear /><add key=\"nuget.org\" value=\"https://api.nuget.org/v3/index.json\" /></packageSources></configuration>");

        var folder = LocalFeed.Of(template);
        folder.ShouldBe(Path.GetFullPath(feed.Path));
        LocalFeed.Of(feed.Path).ShouldBe(folder);
        LocalFeed.Of(null).ShouldBeNull();
        LocalFeed.Add(app.Path, folder!);
        LocalFeed.Add(app.Path, folder!);

        var text = File.ReadAllText(config);
        text.ShouldContain("nuget.org");
        text.ShouldContain($"key=\"{LocalFeed.Name}\" value=\"{folder}\"");
        text.Split(LocalFeed.Name).Length.ShouldBe(2);
    }
}
