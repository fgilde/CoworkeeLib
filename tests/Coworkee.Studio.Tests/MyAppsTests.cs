using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Coworkee.Cli.Spec;
using Coworkee.Studio.Cli;
using Coworkee.Studio.Model;
using Coworkee.Studio.ViewModels;
using Coworkee.Studio.Views;

namespace Coworkee.Studio.Tests;

public sealed class MyAppsTests
{
    [Fact]
    public void A_created_app_is_remembered_in_the_settings_with_its_options()
    {
        using var folder = new TempFolder();
        var app = App(folder, "Shop");
        var main = Studio.Create(folder);

        main.Apps.Remember(app, new AppSpec { Name = "Shop", Options = { ["database"] = "sqlite", ["chat"] = false } });

        var saved = StudioSettings.Load(folder.File("settings.json")).Apps.ShouldHaveSingleItem();
        saved.Name.ShouldBe("Shop");
        saved.Path.ShouldBe(app);
        saved.Options.ShouldBe(new Dictionary<string, string?> { ["chat"] = "false", ["database"] = "sqlite" }, ignoreOrder: true);
        main.Apps.Apps.ShouldHaveSingleItem().OptionsText.ShouldContain("database sqlite");

        var reopened = Studio.Create(folder, settings: StudioSettings.Load(folder.File("settings.json")));
        reopened.Apps.Apps.ShouldHaveSingleItem().Name.ShouldBe("Shop");
    }

    [Fact]
    public void Folders_are_searched_for_app_json_and_app_hosts()
    {
        using var folder = new TempFolder();
        var withState = App(folder, "work/Crm");
        Directory.CreateDirectory(Path.Combine(withState, ".coworkee"));
        File.WriteAllText(Path.Combine(withState, ".coworkee", "app.json"), """{ "name": "Crm", "options": { "keycloak": true } }""");
        App(folder, "work/old/Legacy");
        Directory.CreateDirectory(folder.File("work/notes"));
        var main = Studio.Create(folder);

        main.Apps.AddFolder(folder.File("work"));

        main.Apps.Apps.Select(a => a.Name).ShouldBe(["Crm", "Legacy"], ignoreOrder: true);
        main.Apps.Apps.Single(a => a.Name == "Crm").App.Options["keycloak"].ShouldBe("true");
        main.Settings.AppFolders.ShouldBe([folder.File("work")]);

        main.Apps.Forget(main.Apps.Apps.Single(a => a.Name == "Legacy"));
        StudioSettings.Load(folder.File("settings.json")).Apps.ShouldHaveSingleItem().Name.ShouldBe("Crm");
    }

    [Fact]
    public void The_status_comes_from_the_app_host_process_or_a_taken_port()
    {
        using var folder = new TempFolder();
        var app = App(folder, "Shop");
        var processes = new HashSet<string>();
        var ports = new HashSet<int>();
        var killed = new List<string>();
        var main = Studio.Create(folder);
        main.Apps.Probe = new AppProbe { HasProcess = processes.Contains, IsListening = ports.Contains, Kill = killed.Add };
        main.Apps.Remember(app, new AppSpec { Name = "Shop" });
        var shop = main.Apps.Apps.Single();

        shop.IsRunning.ShouldBeFalse();
        shop.Links.ShouldBeEmpty();

        ports.Add(17100);
        main.Apps.Refresh();
        shop.Detected.ShouldBe(AppStatus.PortTaken);
        shop.IsRunning.ShouldBeFalse();

        processes.Add("Shop.AppHost");
        main.Apps.Refresh();
        shop.IsRunning.ShouldBeTrue();
        shop.StatusText.ShouldBe("Running outside the Studio");
        shop.Links.Select(l => l.Name).ShouldBe(["Dashboard", "Web", "Grafana", "WebDataStudio"]);
        shop.Links[0].Url.ShouldBe("https://localhost:17100");

        shop.Stop();
        killed.ShouldBe(["Shop.AppHost"]);
    }

    [Fact]
    public async Task Start_and_stop_use_the_runner_of_the_create_page()
    {
        using var folder = new TempFolder();
        var app = App(folder, "Shop");
        var running = new TaskCompletionSource();
        var main = Studio.Create(folder, async (start, output, cancellationToken) =>
        {
            output("      Login to the dashboard at https://localhost:17100/login?t=abc", false);
            output("      Distributed application started. Press Ctrl+C to shut down.", false);
            running.SetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        });
        main.Apps.Dispatch = action => action();
        main.Apps.Probe = new AppProbe { HasProcess = _ => false, IsListening = _ => false, Kill = _ => throw new InvalidOperationException("not ours") };
        main.Apps.Remember(app, new AppSpec { Name = "Shop", Options = { ["grafana"] = false } });
        var shop = main.Apps.Apps.Single();

        shop.Start();
        await running.Task;

        shop.IsRunning.ShouldBeTrue();
        shop.StatusText.ShouldBe("The app is running");
        shop.LastLog.ShouldContain("Distributed application started");
        shop.Links.Select(l => l.Name).ShouldBe(["Dashboard", "Web", "WebDataStudio"]);
        shop.Links[0].Url.ShouldBe("https://localhost:17100/login?t=abc");

        shop.Stop();
        for (var i = 0; i < 100 && shop.IsRunning; i++)
        {
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        shop.IsRunning.ShouldBeFalse();
    }

    [Fact]
    public Task My_apps_opens_over_the_wizard_and_closes_again() => TestApp.Ui(() =>
    {
        using var folder = new TempFolder();
        var main = Studio.Create(folder);
        main.Apps.Remember(App(folder, "Shop"), new AppSpec { Name = "Shop" });
        var window = new MainWindow { DataContext = main };
        window.Show();

        main.ShowAppsCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        window.GetVisualDescendants().OfType<MyAppsView>().ShouldHaveSingleItem();
        window.GetVisualDescendants().OfType<TextBlock>().ShouldContain(t => t.Text == "Shop");
        main.WideHero.ShouldBeFalse();

        main.ClosePageCommand.Execute(null);
        main.HasPage.ShouldBeFalse();
        main.WideHero.ShouldBeTrue();
        window.Close();
    });

    // a solution folder as the template creates it: the slnx, the app host and the web project with their launch settings
    private static string App(TempFolder folder, string path)
    {
        var root = folder.File(path);
        var name = Path.GetFileName(root);
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, name + ".slnx"), "<Solution />");
        foreach (var (project, url) in new[] { ("AppHost", "https://localhost:17100"), ("Web", "https://localhost:7300") })
        {
            var dir = Directory.CreateDirectory(Path.Combine(root, "src", $"{name}.{project}", "Properties")).Parent!.FullName;
            File.WriteAllText(Path.Combine(dir, $"{name}.{project}.csproj"), "<Project />");
            File.WriteAllText(Path.Combine(dir, "Properties", "launchSettings.json"), $$"""{ "profiles": { "https": { "applicationUrl": "{{url}}" } } }""");
        }

        return root;
    }
}
