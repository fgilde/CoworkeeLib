using System.Diagnostics;

namespace Coworkee.Studio.Tests;

public sealed class UpdateTests
{
    [Fact]
    public async Task A_newer_studio_shows_a_badge_and_updates_cli_and_studio_after_closing()
    {
        using var folder = new TempFolder();
        var main = Studio.Create(folder);
        var spec = folder.File("shop.json");
        main.Basics.Name = "Shop";
        main.Save(spec);

        await main.Update.CheckAsync(_ => Task.FromResult(new[] { "0.0.1", "99.0.0", "99.1.0-preview.1" }));

        main.Update.HasUpdate.ShouldBeTrue();
        main.Update.Text.ShouldBe(main.Version.Contains('-') ? "Update available 99.1.0-preview.1" : "Update available 99.0.0");
        ProcessStartInfo? helper = null;
        main.Dialogs.Restart = start => helper = start;
        main.Update.Install();
        var script = helper.ShouldNotBeNull().ArgumentList[^1];
        script.ShouldContain("Coworkee.Cli");
        script.ShouldContain("Coworkee.Studio");
        script.ShouldContain(main.Update.Available!);
        script.ShouldContain(spec);
    }

    [Fact]
    public async Task No_network_or_no_newer_version_shows_nothing()
    {
        using var folder = new TempFolder();
        var main = Studio.Create(folder);

        await main.Update.CheckAsync(_ => Task.FromResult(new[] { "0.0.1" }));
        main.Update.HasUpdate.ShouldBeFalse();
        await main.Update.CheckAsync(_ => throw new HttpRequestException("offline"));
        main.Update.HasUpdate.ShouldBeFalse();
        main.Update.Install();
    }
}
