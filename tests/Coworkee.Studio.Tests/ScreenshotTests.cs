using Coworkee.Studio.ViewModels;

namespace Coworkee.Studio.Tests;

/// <summary>Renders every page in light and dark; with STUDIO_SCREENSHOTS=folder the frames are saved there (the website uses them).</summary>
public sealed class ScreenshotTests
{
    private static readonly string? Folder = Environment.GetEnvironmentVariable("STUDIO_SCREENSHOTS");

    public static TheoryData<string, bool, string> Pages()
    {
        var data = new TheoryData<string, bool, string>();
        foreach (var page in new[] { "welcome", "basics", "branding", "signin", "architecture", "ai", "languages", "entities", "entities-diagram", "entities-import", "users", "review", "generate", "run", "help", "gallery", "apps" })
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

        if (page is "generate" or "run")
        {
            Scenario.Generated(main.Generate, main.Basics.Output!);
            var app = main.Generate.App;
            app.WasStarted = true;
            foreach (var line in new[] { "info: Aspire.Hosting.DistributedApplication[0]", "      Login to the dashboard at https://localhost:17100/login?t=••••••••", "      Distributed application started. Press Ctrl+C to shut down." })
            {
                app.Add(line, false);
            }

            app.Endpoints = [new Cli.AppEndpoint("Web", "https://localhost:7300"), new Cli.AppEndpoint("Api", "https://localhost:7301"), new Cli.AppEndpoint("Auth", "https://localhost:7302")];

            // published screenshots show no password, not even a made-up one
            var signIn = main.Generate.SignIn!;
            main.Generate.SignIn = signIn with { Users = [.. signIn.Users.Select(u => u with { Password = new string('•', 16) })] };
        }

        if (page == "entities")
        {
            var order = main.Entities.Modules[0].Entities.Single(e => e.Name == "Order");
            main.Entities.SelectEntity(order);
            order.Properties.Single(p => p.Name == "Customer").IsExpanded = true;
        }

        if (page == "gallery")
        {
            main.ShowGalleryCommand.Execute(null);
        }

        if (page == "apps")
        {
            main.Apps.Probe = new Cli.AppProbe { HasProcess = name => name == "Shop.AppHost", IsListening = _ => false };
            foreach (var name in new[] { "Shop", "Crm" })
            {
                var appHost = Directory.CreateDirectory(folder.File($"apps/{name}/src/{name}.AppHost/Properties")).Parent!.FullName;
                File.WriteAllText(Path.Combine(appHost, $"{name}.AppHost.csproj"), "<Project />");
                File.WriteAllText(Path.Combine(appHost, "Properties", "launchSettings.json"), """{ "profiles": { "https": { "applicationUrl": "https://localhost:17100" } } }""");
                main.Apps.Remember(folder.File($"apps/{name}"), new Coworkee.Cli.Spec.AppSpec { Name = name, Options = { ["database"] = name == "Crm" ? "sqlite" : "postgres", ["keycloak"] = name == "Shop" } });
            }

            main.Page = main.Apps;
        }

        if (page == "entities-diagram")
        {
            main.Entities.ViewMode = "Diagram";
            main.Entities.AddImported(new Coworkee.Cli.Spec.Import.ImportResult(new Coworkee.Cli.Spec.ModuleSpec
            {
                Name = "Sales",
                Entities =
                [
                    new Coworkee.Cli.Spec.EntitySpec
                    {
                        Name = "Invoice",
                        Properties =
                        [
                            new Coworkee.Cli.Spec.PropertySpec { Name = "Number", Required = true, MaxLength = 30 },
                            new Coworkee.Cli.Spec.PropertySpec { Name = "Order", Type = Coworkee.Cli.Spec.PropertyType.Reference, Reference = "Order", Required = true },
                            new Coworkee.Cli.Spec.PropertySpec { Name = "Contact", Type = Coworkee.Cli.Spec.PropertyType.Reference, Reference = "Customer" },
                            new Coworkee.Cli.Spec.PropertySpec { Name = "Amount", Type = Coworkee.Cli.Spec.PropertyType.Money, Required = true },
                            new Coworkee.Cli.Spec.PropertySpec { Name = "Paid", Type = Coworkee.Cli.Spec.PropertyType.Bool },
                        ],
                    },
                ],
            }, [], []));
            main.Entities.SelectEntity(main.Entities.Modules[0].Entities.Single(e => e.Name == "Order"));
        }

        if (page == "entities-import")
        {
            var csv = folder.File("suppliers.csv");
            File.WriteAllText(csv, "Id,Name,Email,Rating,Since" + Environment.NewLine + "1,Acme,info@acme.test,4,2020-01-02");
            main.Entities.OpenImport();
            main.Entities.Import.Kind = "CSV";
            main.Entities.Import.Source = csv;
            main.Entities.Import.Result = Coworkee.Cli.Spec.Import.EntityImporter.ImportAsync(new(Coworkee.Cli.Spec.Import.ImportSource.Csv, csv)).Result;
        }

        if (page == "help")
        {
            main.ShowHelp("2");
        }

        if (page == "ai")
        {
            var ai = main.Architecture.Ai;
            main.Option("ai").IsOn = true;
            ai.Selected = ai.Providers.Single(p => p.Info.Id == Contracts.Ai.AiProviderCatalog.OpenRouter);
            ai.Model = "anthropic/claude-sonnet-4.5";
            ai.ApiKey = "sk-or-example";
        }

        StepViewModel step = page switch
        {
            "welcome" => main.Welcome,
            "basics" => main.Basics,
            "branding" => main.Branding,
            "signin" => main.SignIn,
            "architecture" or "ai" => main.Architecture,
            "languages" => main.Languages,
            "entities" or "entities-diagram" or "entities-import" => main.Entities,
            "users" => main.Users,
            "review" => main.Review,
            "generate" or "run" => main.Generate,
            _ => main.Architecture,
        };

        // the AI card and the running app sit below the fold
        var frame = Render.Step(main, step, dark, page switch
        {
            "ai" => window => ScrollTo(window, "AiSection"),
            "run" => window => ScrollTo(window, "RunCard"),
            _ => null,
        });
        frame.PixelSize.Width.ShouldBe(1400);
        if (Folder is not null)
        {
            Directory.CreateDirectory(Folder);
            frame.Save(Path.Combine(Folder, $"studio-{page}-{(dark ? "dark" : "light")}{(language == "en" ? string.Empty : "-" + language)}.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        }
    });

    private static void ScrollTo(Avalonia.Controls.Window window, string name)
    {
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var target = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window).OfType<Avalonia.Controls.Control>().First(c => c.Name == name);
        Avalonia.Controls.ControlExtensions.BringIntoView(target);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }
}
