using System.Diagnostics;
using Coworkee.Studio.Cli;
using Coworkee.Studio.ViewModels;

namespace Coworkee.Studio.Tests;

public sealed class AppRunTests
{
    private const string Login = "https://localhost:17100/login?t=4f1c2b9e";

    private static readonly string[] Started =
    [
        "info: Aspire.Hosting.DistributedApplication[0]", "      Aspire version: 13.6.1",
        "info: Aspire.Hosting.DistributedApplication[0]", "      Now listening on: https://localhost:17100",
        "info: Aspire.Hosting.DistributedApplication[0]", "      Login to the dashboard at " + Login,
        "info: Aspire.Hosting.DistributedApplication[0]", "      Distributed application started. Press Ctrl+C to shut down.",
    ];

    [Fact]
    public void Reads_the_dashboard_login_the_start_and_errors_from_the_output()
    {
        AspireOutput.DashboardLogin("      Login to the dashboard at " + Login).ShouldBe(Login);
        AspireOutput.DashboardLogin("      Now listening on: https://localhost:17100").ShouldBeNull();
        AspireOutput.IsStarted(Started[^1]).ShouldBeTrue();
        AspireOutput.IsFailHeader("fail: Microsoft.Extensions.Hosting.Internal.Host[11]").ShouldBeTrue();
        AspireOutput.IsError("Unhandled exception. System.IO.IOException: Failed to bind to address https://127.0.0.1:17100: address already in use.").ShouldBeTrue();
        AspireOutput.IsError("src/Shop.Api/Program.cs(3,1): error CS1002: ; expected").ShouldBeTrue();
        AspireOutput.IsError("    0 Error(s)").ShouldBeFalse();
        AspireOutput.IsError("      Distributed application starting.").ShouldBeFalse();
    }

    [Fact]
    public void Finds_the_addresses_of_the_app_in_the_launch_settings()
    {
        using var folder = new TempFolder();
        Project(folder, "Shop.AppHost", "https://localhost:17100;http://localhost:15100");
        Project(folder, "Shop.Api", "https://localhost:7301;http://localhost:5301");
        Project(folder, "Shop.Web", "http://localhost:5300;https://localhost:7300");

        AspireOutput.Endpoints(folder.Path).ShouldBe([new AppEndpoint("Web", "https://localhost:7300"), new AppEndpoint("Api", "https://localhost:7301")]);
        AspireOutput.Endpoints(folder.File("missing")).ShouldBeEmpty();
    }

    [Fact]
    public async Task Runs_the_app_host_shows_the_links_and_stops_it()
    {
        using var folder = new TempFolder();
        Project(folder, "Shop.AppHost", "https://localhost:17100");
        Project(folder, "Shop.Web", "https://localhost:7300");
        ProcessStartInfo? started = null;
        var running = new TaskCompletionSource();
        var main = Studio.Create(folder, async (start, output, cancellationToken) =>
        {
            started = start;
            foreach (var line in Started)
            {
                output(line, false);
            }

            running.SetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        });
        var app = main.Generate.App;
        main.Generate.Output = folder.Path;

        var run = app.StartAsync();
        await running.Task;

        started!.ArgumentList.ShouldBe(["run", "--project", Path.Combine(folder.Path, "src", "Shop.AppHost", "Shop.AppHost.csproj")]);
        app.State.ShouldBe(AppRunState.Running);
        app.DashboardUrl.ShouldBe(Login);
        app.Endpoints.ShouldBe([new AppEndpoint("Web", "https://localhost:7300")]);
        app.Log.Count.ShouldBe(Started.Length + 1);
        string? opened = null;
        main.Dialogs.Open = url => opened = url;
        app.Open(app.DashboardUrl!);
        opened.ShouldBe(Login);

        app.Stop();
        await run;
        app.State.ShouldBe(AppRunState.Stopped);
    }

    [Fact]
    public async Task A_failed_start_shows_the_line_that_explains_it()
    {
        using var folder = new TempFolder();
        Project(folder, "Shop.AppHost", "https://localhost:17100");
        var main = Studio.Create(folder, (_, output, _) =>
        {
            output("info: Aspire.Hosting.DistributedApplication[0]", false);
            output("fail: Microsoft.Extensions.Hosting.Internal.Host[11]", false);
            output("      Hosting failed to start", false);
            output("      System.IO.IOException: Failed to bind to address https://127.0.0.1:17100: address already in use.", false);
            return Task.FromResult(1);
        });
        main.Generate.Output = folder.Path;

        await main.Generate.App.StartAsync();

        main.Generate.App.State.ShouldBe(AppRunState.Failed);
        main.Generate.App.StatusText.ShouldBe("Hosting failed to start");
        main.Generate.App.ShowLog.ShouldBeTrue();
    }

    [Fact]
    public async Task Without_an_app_host_it_says_so()
    {
        using var folder = new TempFolder();
        var main = Studio.Create(folder);
        main.Generate.Output = folder.Path;

        await main.Generate.App.StartAsync();

        main.Generate.App.IsFailed.ShouldBeTrue();
        main.Generate.App.StatusText.ShouldContain(folder.Path);
    }

    private static void Project(TempFolder folder, string name, string urls)
    {
        var properties = Directory.CreateDirectory(Path.Combine(folder.Path, "src", name, "Properties")).FullName;
        File.WriteAllText(Path.Combine(folder.Path, "src", name, name + ".csproj"), "<Project />");
        File.WriteAllText(Path.Combine(properties, "launchSettings.json"), $$"""{ "profiles": { "https": { "commandName": "Project", "applicationUrl": "{{urls}}" } } }""");
    }
}
