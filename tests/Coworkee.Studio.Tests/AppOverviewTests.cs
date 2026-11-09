using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Coworkee.Cli.Spec;
using Coworkee.Studio.Cli;
using Coworkee.Studio.ViewModels;
using Coworkee.Studio.Views;

namespace Coworkee.Studio.Tests;

public sealed class AppOverviewTests
{
    // as coworkee deploy azure --apply writes it, before it kept the tools
    private const string CliRecord = """
        {
          "name": "rg-shop",
          "target": "azure",
          "subscriptionId": "sub-1",
          "tenantId": "tenant-1",
          "resourceGroup": "rg-shop",
          "location": "germanywestcentral",
          "account": "me@example.com",
          "deployedAt": "2026-11-08T10:15:00.0000000+01:00",
          "urls": {
            "shop-api": "https://shop-api.example.io",
            "shop-web": "https://shop-web.example.io"
          }
        }
        """;

    [Fact]
    public async Task Opening_an_existing_app_shows_its_overview_with_its_deployments_and_leaves_the_wizard_alone()
    {
        using var folder = new TempFolder();
        var root = App(folder);
        Directory.CreateDirectory(Deployments.Folder(root));
        await File.WriteAllTextAsync(Deployments.RecordPath(root, "rg-shop"), CliRecord, TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Deployments.SecretsPath(root, "rg-shop"), """{ "shop-admin-password": "secret" }""", TestContext.Current.CancellationToken);
        var main = Studio.Create(folder);
        main.Dialogs.PickFolder = _ => Task.FromResult<string?>(Path.Combine(root, "src"));

        await main.OpenAppAsync();

        var overview = main.Page.ShouldBeOfType<AppOverviewViewModel>();
        overview.App.Name.ShouldBe("Shop");
        overview.App.Path.ShouldBe(root);
        main.ExistingApp.ShouldBeNull();
        main.Current.ShouldBe(main.Welcome);
        var deployment = overview.Deployments.ShouldHaveSingleItem();
        deployment.Title.ShouldBe("rg-shop");
        deployment.TargetText.ShouldBe("Azure Container Apps");
        deployment.Where.ShouldBe("Resource group rg-shop in germanywestcentral");
        deployment.Details.ShouldStartWith("me@example.com · last deployed ");
        deployment.Links.ShouldBe([new AppEndpoint("shop-api", "https://shop-api.example.io"), new AppEndpoint("shop-web", "https://shop-web.example.io")]);
        deployment.Record.Tools.ShouldBeEmpty();
        deployment.CanUpdate.ShouldBeTrue();
        main.Welcome.Recent.First().Path.ShouldBe(root);

        overview.Edit();

        main.HasPage.ShouldBeFalse();
        main.ExistingApp.ShouldBe(root);
        main.Current.ShouldBe(main.Review);
    }

    [Fact]
    public void An_app_without_deployments_has_an_empty_list_and_a_folder_without_an_app_stays_on_the_page()
    {
        using var folder = new TempFolder();
        var main = Studio.Create(folder);

        main.ShowApp(App(folder));
        main.Page.ShouldBeOfType<AppOverviewViewModel>().HasDeployments.ShouldBeFalse();

        main.ClosePage();
        main.ShowApp(folder.Path);
        main.HasPage.ShouldBeFalse();
        main.Status.ShouldStartWith("No Coworkee app in");
    }

    [Fact]
    public async Task Deploy_signs_in_picks_subscription_and_resource_group_then_runs_the_cli_and_shows_the_result()
    {
        using var folder = new TempFolder();
        var root = App(folder);
        var az = new FakeAzure { SignedIn = true };
        ProcessStartInfo? deploy = null;
        var main = Studio.Create(folder, (start, output, cancellationToken) =>
        {
            if (!start.ArgumentList.Contains("deploy"))
            {
                return az.Run(start, output, cancellationToken);
            }

            deploy = start;
            output("Deployed Shop to rg-new:", false);
            var record = new DeploymentRecord("rg-new", "azure", "sub-2", "tenant-1", "rg-new", "swedencentral", "me@example.com", DateTimeOffset.Now,
                new Dictionary<string, string> { ["shop-web"] = "https://shop-web.example.io" });
            return Deployments.WriteAsync(root, record, cancellationToken).ContinueWith(_ => 0, TaskScheduler.Default);
        });
        main.Apps.Dispatch = action => action();
        main.ShowApp(root);
        var overview = (AppOverviewViewModel)main.Page!;

        await overview.DeployAsync();

        var wizard = main.Page.ShouldBeOfType<AzureDeployViewModel>();
        wizard.IsSignedIn.ShouldBeTrue();
        wizard.Account.ShouldBe("me@example.com");
        wizard.Step.ShouldBe(AzureStep.SignIn);
        await wizard.NextAsync();
        wizard.Subscriptions.Select(s => s.Name).ShouldBe(["Test", "Production"]);
        wizard.Subscription!.Id.ShouldBe("sub-2");
        await wizard.NextAsync();
        wizard.Step.ShouldBe(AzureStep.Target);
        wizard.Groups.Select(g => g.Name).ShouldBe(["rg-old"]);
        wizard.AdminEmail.ShouldBe("me@example.com");
        wizard.CanGoNext.ShouldBeFalse();

        wizard.PickGroup(wizard.Groups[0]);
        wizard.Location.ShouldBe("westus3");
        wizard.Regions.ShouldContain("westus3");
        wizard.IsNewGroup.ShouldBeFalse();
        wizard.ResourceGroup = "rg-new";
        wizard.Location = "swedencentral";
        wizard.Name.ShouldBe("rg-new");
        wizard.IsNewGroup.ShouldBeTrue();
        wizard.ResourceGroup = "bad name.";
        wizard.GroupError.ShouldNotBeNull();
        wizard.CanGoNext.ShouldBeFalse();
        wizard.ResourceGroup = "rg-new";
        wizard.Tools.Single(t => t.Name == "grafana").IsOn = false;
        wizard.Tools.Single(t => t.Name == "mailpit").IsOn = true;
        await wizard.NextAsync();

        wizard.Step.ShouldBe(AzureStep.Summary);
        wizard.ScopeText.ShouldBe("Everything is created in resource group rg-new only; nothing else in the subscription is touched.");
        wizard.GroupText.ShouldBe("rg-new (new)");
        wizard.ToolsText.ShouldBe("+mailpit, -grafana");
        deploy.ShouldBeNull();
        az.Calls.ShouldAllBe(c => c.StartsWith("account ", StringComparison.Ordinal) || c.StartsWith("group list", StringComparison.Ordinal));

        await wizard.DeployAsync();

        deploy!.WorkingDirectory.ShouldBe(root);
        deploy.RedirectStandardInput.ShouldBeTrue();
        deploy.ArgumentList.SkipWhile(a => a != "deploy").ShouldBe(
        [
            "deploy", "azure", "--apply", "--yes", "--resource-group", "rg-new", "--location", "swedencentral", "--subscription", "sub-2", "--name", "rg-new",
            "--admin-email", "me@example.com", "--with", "mailpit", "--without", "grafana",
        ]);
        wizard.IsDone.ShouldBeTrue();
        wizard.DeployStatus.ShouldBe("Deployed to rg-new.");
        wizard.Links.ShouldBe([new AppEndpoint("shop-web", "https://shop-web.example.io")]);
        wizard.PasswordText.ShouldContain(Deployments.SecretsPath(root, "rg-new"));
        wizard.Run.Log.ShouldContain("Deployed Shop to rg-new:");
        overview.Deployments.ShouldHaveSingleItem().Record.ResourceGroup.ShouldBe("rg-new");

        wizard.Close();
        main.Page.ShouldBe(overview);
    }

    [Fact]
    public async Task Signed_out_the_wizard_runs_az_login_and_checks_again()
    {
        using var folder = new TempFolder();
        var az = new FakeAzure();
        var main = Studio.Create(folder, az.Run);
        main.ShowApp(App(folder));

        await ((AppOverviewViewModel)main.Page!).DeployAsync();
        var wizard = (AzureDeployViewModel)main.Page!;

        wizard.IsSignedOut.ShouldBeTrue();
        wizard.CanGoNext.ShouldBeFalse();
        wizard.Problem.ShouldBe("Please run 'az login' to setup account.");

        await wizard.SignInAsync();

        az.Calls.ShouldContain("login -o none");
        wizard.IsSignedIn.ShouldBeTrue();
        wizard.HasProblem.ShouldBeFalse();
        wizard.CanGoNext.ShouldBeTrue();
    }

    [Fact]
    public async Task The_windows_broker_problem_is_recognized_and_fixed_with_the_config_switch_and_a_new_sign_in()
    {
        using var folder = new TempFolder();
        var az = new FakeAzure { SignedIn = true, Broker = true };
        var main = Studio.Create(folder, az.Run);
        main.ShowApp(App(folder));

        await ((AppOverviewViewModel)main.Page!).DeployAsync();
        var wizard = (AzureDeployViewModel)main.Page!;

        wizard.IsBrokerProblem.ShouldBeTrue();
        wizard.Account.ShouldBe("me@example.com");
        AzureDeployViewModel.BrokerFixCommands.ShouldBe("az config set core.enable_broker_on_windows=false\naz login");

        await wizard.FixBrokerAsync();

        az.Calls.ShouldContain("config set core.enable_broker_on_windows=false");
        az.Calls.IndexOf("login -o none").ShouldBeGreaterThan(az.Calls.IndexOf("config set core.enable_broker_on_windows=false"));
        wizard.IsSignedIn.ShouldBeTrue();
    }

    [Fact]
    public async Task Without_the_azure_cli_the_wizard_offers_the_install_link()
    {
        using var folder = new TempFolder();
        string? opened = null;
        var main = Studio.Create(folder);
        main.Dialogs.Open = url => opened = url;
        main.ShowApp(App(folder));

        await ((AppOverviewViewModel)main.Page!).DeployAsync();
        var wizard = (AzureDeployViewModel)main.Page!;
        wizard.IsNotInstalled.ShouldBeTrue();
        wizard.Open(AzureDeployViewModel.InstallUrl);

        opened.ShouldBe("https://learn.microsoft.com/cli/azure/install-azure-cli");
    }

    [Fact]
    public async Task Update_takes_the_values_of_the_record_and_goes_straight_to_the_summary()
    {
        using var folder = new TempFolder();
        var root = App(folder);
        await Deployments.WriteAsync(root, new DeploymentRecord("prod", "azure", "sub-1", "tenant-1", "rg-shop", "westeurope", "me@example.com", DateTimeOffset.Now,
            new Dictionary<string, string>()) { Tools = new Dictionary<string, bool> { ["grafana"] = false } }, TestContext.Current.CancellationToken);
        var az = new FakeAzure { SignedIn = true };
        var main = Studio.Create(folder, az.Run);
        main.ShowApp(root);
        var overview = (AppOverviewViewModel)main.Page!;

        await overview.UpdateAsync(overview.Deployments.Single());

        var wizard = main.Page.ShouldBeOfType<AzureDeployViewModel>();
        wizard.IsUpdate.ShouldBeTrue();
        wizard.Title.ShouldBe("Update prod");
        wizard.Step.ShouldBe(AzureStep.Summary);
        wizard.Subscription!.Name.ShouldBe("Test");
        wizard.GroupText.ShouldBe("rg-shop");
        wizard.AdminText.ShouldBe("as before");
        wizard.Arguments().ShouldBe(
            ["azure", "--apply", "--yes", "--resource-group", "rg-shop", "--location", "westeurope", "--subscription", "sub-1", "--name", "prod", "--without", "grafana"]);
    }

    [Fact]
    public Task The_overview_and_the_wizard_render_over_the_wizard_steps() => TestApp.Ui(() =>
    {
        using var folder = new TempFolder();
        var root = App(folder);
        Directory.CreateDirectory(Deployments.Folder(root));
        File.WriteAllText(Deployments.RecordPath(root, "rg-shop"), CliRecord);
        var main = Studio.Create(folder, new FakeAzure { SignedIn = true }.Run);
        var window = new MainWindow { DataContext = main };
        window.Show();

        main.ShowApp(root);
        Dispatcher.UIThread.RunJobs();
        window.GetVisualDescendants().OfType<AppOverviewView>().ShouldHaveSingleItem();
        window.GetVisualDescendants().OfType<TextBlock>().ShouldContain(t => t.Text == "Resource group rg-shop in germanywestcentral");

        var update = ((AppOverviewViewModel)main.Page!).UpdateAsync(((AppOverviewViewModel)main.Page!).Deployments[0]);
        update.IsCompleted.ShouldBeTrue();
        Dispatcher.UIThread.RunJobs();
        window.GetVisualDescendants().OfType<AzureDeployView>().ShouldHaveSingleItem();
        window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "ScopeLine").Text
            .ShouldBe("Everything is created in resource group rg-shop only; nothing else in the subscription is touched.");
        window.Close();
    });

    private static string App(TempFolder folder)
    {
        var root = Directory.CreateDirectory(Path.Combine(folder.Path, "Shop")).FullName;
        File.WriteAllText(Path.Combine(root, "Shop.slnx"), "<Solution />");
        Directory.CreateDirectory(Path.Combine(root, "src", "Shop.AppHost"));
        File.WriteAllText(Path.Combine(root, "src", "Shop.AppHost", "AppHost.cs"), "builder.AddCoworkeeApp(\"shop\");\n");
        return root;
    }

    /// <summary>The Azure CLI as far as the wizard asks it; it records every call and touches nothing.</summary>
    private sealed class FakeAzure
    {
        public bool SignedIn { get; set; }

        public bool Broker { get; set; }

        public List<string> Calls { get; } = [];

        public Task<int> Run(ProcessStartInfo start, Action<string, bool> output, CancellationToken cancellationToken)
        {
            start.FileName.ShouldBe(Deployments.AzureCli);
            var call = string.Join(' ', start.ArgumentList);
            Calls.Add(call);
            switch (call)
            {
                case "login -o none":
                    SignedIn = true;
                    return Task.FromResult(0);
                case "config set core.enable_broker_on_windows=false":
                    Broker = false;
                    return Task.FromResult(0);
                case "account show -o json" when SignedIn:
                    output("""{ "id": "sub-2", "name": "Production", "tenantId": "tenant-1", "user": { "name": "me@example.com", "type": "user" } }""", false);
                    return Task.FromResult(0);
                case "account show -o json":
                    output("Please run 'az login' to setup account.", true);
                    return Task.FromResult(1);
                case "account get-access-token --subscription sub-2 --query expiresOn -o tsv" when Broker:
                    output("ERROR: Can't find token from MSAL cache.", true);
                    return Task.FromResult(1);
                case "account get-access-token --subscription sub-2 --query expiresOn -o tsv":
                    output("2026-11-09 12:00:00.000000", false);
                    return Task.FromResult(0);
                case "account list -o json":
                    output("""[{ "id": "sub-1", "name": "Test", "tenantId": "tenant-1", "isDefault": false }, { "id": "sub-2", "name": "Production", "tenantId": "tenant-1", "isDefault": true }]""", false);
                    return Task.FromResult(0);
                case "group list --subscription sub-2 -o json":
                    output("""[{ "name": "rg-old", "location": "westus3" }]""", false);
                    return Task.FromResult(0);
                case "group list --subscription sub-1 -o json":
                    output("""[{ "name": "rg-shop", "location": "westeurope" }]""", false);
                    return Task.FromResult(0);
                default:
                    throw new InvalidOperationException($"unexpected az {call}");
            }
        }
    }
}
