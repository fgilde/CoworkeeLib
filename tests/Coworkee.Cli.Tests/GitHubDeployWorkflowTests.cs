using System.Diagnostics;
using Coworkee.Cli.Apps;
using Coworkee.Cli.Commands;
using Coworkee.Cli.Spec;

namespace Coworkee.Cli.Tests;

public sealed class GitHubDeployWorkflowTests
{
    private static readonly DeployWorkflowSettings Defaults = new("azure", null, "germanywestcentral", ["main"], false);

    [Fact]
    public void The_workflow_deploys_each_listed_branch_into_its_own_environment_and_resource_group()
    {
        using var folder = new TempFolder();
        folder.File("Shop.slnx");
        folder.File("global.json");
        folder.File("src/Shop.AirWorks/Shop.AirWorks.csproj");

        var yaml = GitHubDeployWorkflow.Yaml(Solution.Find(folder.Path), Defaults with { Branches = ["main", "release/*"], TeardownOnDelete = true });

        yaml.ShouldContain("branches: [main, 'release/*']");
        yaml.ShouldContain("workflow_dispatch:\n  delete:\n");
        yaml.ShouldContain("RESOURCE_GROUP_PREFIX: shop\n");
        yaml.ShouldContain("LOCATION: germanywestcentral\n");
        yaml.ShouldContain("environment: ${{ github.ref_name }}");
        yaml.ShouldContain("global-json-file: global.json");
        yaml.ShouldContain("node-version: 22");
        yaml.ShouldContain("dotnet workload install wasm-tools");
        yaml.ShouldContain("dotnet tool install -g Coworkee.Cli --version \"$version\"");
        yaml.ShouldContain("subscription-id: ${{ secrets.AZURE_SUBSCRIPTION_ID }}");
        yaml.ShouldNotContain("client-secret");
        yaml.ShouldContain("SHOP_ADMIN_EMAIL: ${{ vars.SHOP_ADMIN_EMAIL }}");
        yaml.ShouldContain("SHOP_ADMIN_PASSWORD: ${{ secrets.SHOP_ADMIN_PASSWORD }}");
        yaml.ShouldContain("SHOP_SMTP_PASSWORD: ${{ secrets.SHOP_SMTP_PASSWORD }}");
        yaml.ShouldContain("coworkee deploy azure --resource-group \"$GROUP\" --location \"$LOCATION\" --name \"$NAME\" --apply --yes");
        yaml.ShouldContain("gh secret set COWORKEE_DEPLOY_SECRETS --env \"$BRANCH\"");
        yaml.ShouldContain("github.event.ref != github.event.repository.default_branch");
        yaml.ShouldContain("(github.event.ref == 'main' || startsWith(github.event.ref, 'release/'))");
        yaml.ShouldContain("az group delete -n \"$GROUP\" --yes --no-wait");
        yaml.ShouldNotContain("{{{");
        yaml.ShouldNotContain("\t");
        yaml.ShouldNotContain("\r");
    }

    [Fact]
    public void Without_teardown_a_prefix_global_json_or_airworks_the_workflow_leaves_them_out()
    {
        using var folder = new TempFolder();
        folder.File("Shop.slnx");
        var solution = Solution.Find(folder.Path);

        var yaml = GitHubDeployWorkflow.Yaml(solution, Defaults with { ResourceGroupPrefix = "shop-preview", Location = "westeurope", Branches = [] });

        yaml.ShouldContain("branches: [main]");
        yaml.ShouldContain("RESOURCE_GROUP_PREFIX: shop-preview\n");
        yaml.ShouldContain("LOCATION: westeurope\n");
        yaml.ShouldContain("dotnet-version: 10.0.x");
        yaml.ShouldNotContain("setup-node");
        yaml.ShouldNotContain("delete:");
        yaml.ShouldNotContain("teardown");
        yaml.ShouldNotContain("az group delete");
        Should.Throw<CliException>(() => GitHubDeployWorkflow.Yaml(solution, Defaults with { ResourceGroupPrefix = "Shop_RG" }));
    }

    [Theory]
    [InlineData("main", "main", "shop-main")]
    [InlineData("Feature/JIRA-12_new UI", "feature-jira-12-new-ui", "shop-feature-jira-12-new-ui")]
    [InlineData("--release/1.2--", "release-1-2", "shop-release-1-2")]
    public async Task The_branch_becomes_a_valid_deployment_name_and_resource_group(string branch, string name, string group)
    {
        var lines = await NamesAsync("shop", branch);

        lines.ShouldBe([$"NAME={name}", $"GROUP={group}"]);
    }

    [Fact]
    public async Task The_resource_group_is_cut_to_90_characters_without_a_trailing_dash()
    {
        var lines = await NamesAsync("shop", new string('a', 84) + "-b-cccc");

        lines[1].ShouldBe($"GROUP=shop-{new string('a', 84)}");
    }

    [Fact]
    public async Task A_workflow_changed_by_hand_is_overwritten_only_with_force_while_its_own_is_regenerated()
    {
        using var folder = new TempFolder();
        folder.File("Shop.slnx");
        var solution = Solution.Find(folder.Path);
        var path = await GitHubDeployWorkflow.WriteAsync(solution, Defaults, force: false, CancellationToken.None);
        DeployWorkflowSettings.IsChangedByHand(File.ReadAllText(path)).ShouldBeFalse();
        // git on Windows checks it out with CRLF: no change
        DeployWorkflowSettings.IsChangedByHand(File.ReadAllText(path).Replace("\n", "\r\n")).ShouldBeFalse();

        await GitHubDeployWorkflow.WriteAsync(solution, Defaults with { Location = "westeurope" }, force: false, CancellationToken.None);
        File.ReadAllText(path).ShouldContain("LOCATION: westeurope\n");

        File.AppendAllText(path, "# mine\n");
        DeployWorkflowSettings.IsChangedByHand(File.ReadAllText(path)).ShouldBeTrue();
        await Should.ThrowAsync<CliException>(() => GitHubDeployWorkflow.WriteAsync(solution, Defaults, force: false, CancellationToken.None));
        File.ReadAllText(path).ShouldEndWith("# mine\n");
        GitHubDeployWorkflow.Remove(solution)!.ShouldContain("changed by hand");
        File.Exists(path).ShouldBeTrue();

        await GitHubDeployWorkflow.WriteAsync(solution, Defaults, force: true, CancellationToken.None);
        File.ReadAllText(path).ShouldNotContain("# mine");
        DeployWorkflowSettings.IsChangedByHand("name: mine\n").ShouldBeTrue();
    }

    [Fact]
    public async Task Coworkee_add_writes_the_workflow_from_the_options_and_remove_takes_it_away()
    {
        using var folder = new TempFolder();
        folder.File("Shop.slnx");
        Directory.CreateDirectory(Path.Combine(folder.Path, "src"));
        var solution = Solution.Find(folder.Path);
        var current = new AppSpec { Name = "Shop" };
        var options = new ApplyOptions(DryRun: false, RequireClean: false, Migration: false);

        var on = OptionChanges.Apply(current, ["deployWorkflow", "azure", "deployLocation", "westeurope", "deployBranches", "main,develop", "deployTeardown"], remove: false);
        (await AddCommand.RunAsync(solution, current, on, "x", options, CancellationToken.None)).ShouldBe(0);

        var path = Path.Combine(folder.Path, GitHubDeployWorkflow.RelativePath);
        File.ReadAllText(path).ShouldContain("branches: [main, develop]");
        File.ReadAllText(path).ShouldContain("LOCATION: westeurope\n");
        File.ReadAllText(path).ShouldContain("teardown:");
        var stored = AppState.Read(solution).Spec;
        DeployWorkflowSettings.Of(stored).ShouldSatisfyAllConditions(
            s => s.Target.ShouldBe("azure"), s => s.Location.ShouldBe("westeurope"), s => s.Branches.ShouldBe(["main", "develop"]), s => s.TeardownOnDelete.ShouldBeTrue());

        var off = OptionChanges.Apply(stored, ["deployWorkflow"], remove: true);
        (await AddCommand.RunAsync(solution, stored, off, "x", options, CancellationToken.None)).ShouldBe(0);
        File.Exists(path).ShouldBeFalse();
    }

    [Fact]
    public async Task The_settings_of_an_app_without_app_json_are_read_from_its_workflow()
    {
        using var folder = new TempFolder();
        folder.File("Shop.slnx");
        var solution = Solution.Find(folder.Path);
        await GitHubDeployWorkflow.WriteAsync(solution, new DeployWorkflowSettings("azure", "acme", "westeurope", ["main", "release/*"], true), force: false, CancellationToken.None);

        var settings = DeployWorkflowSettings.Of(ExistingApp.Infer(folder.Path));

        settings.ShouldSatisfyAllConditions(
            s => s.Target.ShouldBe("azure"), s => s.ResourceGroupPrefix.ShouldBe("acme"), s => s.Location.ShouldBe("westeurope"),
            s => s.Branches.ShouldBe(["main", "release/*"]), s => s.TeardownOnDelete.ShouldBeTrue());
        DeployWorkflowSettings.Of(new AppSpec { Name = "Shop" }).ShouldSatisfyAllConditions(
            s => s.IsOn.ShouldBeFalse(), s => s.Location.ShouldBe("germanywestcentral"), s => s.Branches.ShouldBe(["main"]));
    }

    [Theory]
    [InlineData("deploy azure --github")]
    [InlineData("deploy azure --github -l westeurope --resource-group-prefix shop --branches main develop --teardown-on-delete true --force")]
    [InlineData("new Shop --deploy-workflow azure --deploy-location westeurope --deploy-branches main,develop --deploy-teardown")]
    public void The_github_options_parse(string args) => CliApp.Build().Parse(args).Errors.ShouldBeEmpty();

    // the names are computed by bash in the workflow; the test runs the same script (Git's bash on Windows: System32's bash is WSL)
    private static async Task<string[]> NamesAsync(string prefix, string branch)
    {
        var gitBash = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "bin", "bash.exe");
        var start = new ProcessStartInfo(OperatingSystem.IsWindows() && File.Exists(gitBash) ? gitBash : "bash")
        {
            ArgumentList = { "-c", GitHubDeployWorkflow.NamesScript.Replace("\r\n", "\n") },
            RedirectStandardOutput = true,
            Environment = { ["RESOURCE_GROUP_PREFIX"] = prefix, ["BRANCH"] = branch },
        };
        using var process = Process.Start(start)!;
        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        process.ExitCode.ShouldBe(0);
        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }
}
