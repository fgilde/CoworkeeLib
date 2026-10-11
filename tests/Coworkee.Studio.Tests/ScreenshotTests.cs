using Coworkee.Studio.ViewModels;

namespace Coworkee.Studio.Tests;

/// <summary>Renders every page in light and dark; with STUDIO_SCREENSHOTS=folder the frames are saved there (the website uses them).</summary>
public sealed class ScreenshotTests
{
    private static readonly string? Folder = Environment.GetEnvironmentVariable("STUDIO_SCREENSHOTS");

    public static TheoryData<string, bool, string> Pages()
    {
        var data = new TheoryData<string, bool, string>();
        foreach (var page in new[] { "welcome", "basics", "branding", "signin", "architecture", "entities", "review", "generate", "help" })
        {
            data.Add(page, false, "en");
            data.Add(page, true, "en");
            data.Add(page, false, "de");
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Pages))]
    public Task Renders_the_page(string page, bool dark, string language) => TestApp.Ui(() =>
    {
        using var folder = new TempFolder();
        var main = Scenario.Shop(folder, OperatingSystem.IsWindows() ? @"C:\work" : "/home/me/work");
        Localization.L.Use(language);
        if (page == "welcome")
        {
            main = Studio.Create(folder);
            Localization.L.Use(language);
            foreach (var name in new[] { "Portal", "Crm", "Shop" })
            {
                var file = Path.Combine(Directory.CreateDirectory(folder.File(name.ToLowerInvariant())).FullName, name.ToLowerInvariant() + ".json");
                File.WriteAllText(file, $$"""{ "name": "{{name}}" }""");
                main.Settings.AddRecent(file);
            }

            main.Welcome.Refresh();
        }

        if (page == "generate")
        {
            Scenario.Generated(main.Generate, main.Basics.Output!);
        }

        if (page == "entities")
        {
            var order = main.Entities.Modules[0].Entities.Single(e => e.Name == "Order");
            main.Entities.SelectEntity(order);
            order.Properties.Single(p => p.Name == "Customer").IsExpanded = true;
        }

        if (page == "help")
        {
            main.ShowHelp("2");
        }

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

        var frame = Render.Step(main, step, dark);
        frame.PixelSize.Width.ShouldBe(1400);
        if (Folder is not null)
        {
            Directory.CreateDirectory(Folder);
            frame.Save(Path.Combine(Folder, $"studio-{page}-{(dark ? "dark" : "light")}{(language == "en" ? string.Empty : "-" + language)}.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        }
    });
}
