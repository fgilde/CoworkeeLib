using Coworkee.Studio.ViewModels;

namespace Coworkee.Studio.Tests;

/// <summary>Renders every page in light and dark; with STUDIO_SCREENSHOTS=folder the frames are saved there (the website uses them).</summary>
public sealed class ScreenshotTests
{
    private static readonly string? Folder = Environment.GetEnvironmentVariable("STUDIO_SCREENSHOTS");

    public static TheoryData<string, bool> Pages()
    {
        var data = new TheoryData<string, bool>();
        foreach (var page in new[] { "welcome", "basics", "branding", "signin", "architecture", "entities", "review", "generate", "help" })
        {
            data.Add(page, false);
            data.Add(page, true);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Pages))]
    public Task Renders_the_page(string page, bool dark) => TestApp.Ui(() =>
    {
        using var folder = new TempFolder();
        var main = Scenario.Shop(folder);
        StepViewModel step = page switch
        {
            "welcome" => main.Welcome,
            "basics" => main.Basics,
            "branding" => main.Branding,
            "signin" => main.SignIn,
            "architecture" => main.Architecture,
            "entities" => main.Entities,
            "review" => main.Review,
            "generate" => main.Generate,
            _ => main.Architecture,
        };
        if (page == "welcome")
        {
            main = Studio.Create(folder);
            foreach (var name in new[] { "Portal", "Crm", "Shop" })
            {
                File.WriteAllText(folder.File(name.ToLowerInvariant() + ".json"), $$"""{ "name": "{{name}}" }""");
                main.Settings.AddRecent(folder.File(name.ToLowerInvariant() + ".json"));
            }

            main.Welcome.Refresh();
        }

        if (page == "generate")
        {
            Scenario.Generated(main.Generate, Path.Combine(folder.Path, "Shop"));
        }

        if (page == "help")
        {
            main.ShowHelp("2");
        }

        var frame = Render.Step(main, step, dark);
        frame.PixelSize.Width.ShouldBe(1400);
        if (Folder is not null)
        {
            Directory.CreateDirectory(Folder);
            frame.Save(Path.Combine(Folder, $"studio-{page}-{(dark ? "dark" : "light")}.png"));
        }
    });
}
