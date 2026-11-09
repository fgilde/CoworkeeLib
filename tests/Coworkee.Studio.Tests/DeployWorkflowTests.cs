using System.Diagnostics;
using Coworkee.Cli.Spec;

namespace Coworkee.Studio.Tests;

public sealed class DeployWorkflowTests
{
    [Fact]
    public async Task The_dialog_shows_the_saved_settings_and_saving_runs_coworkee_deploy_azure_github_with_them()
    {
        using var folder = new TempFolder();
        File.WriteAllText(Path.Combine(folder.Path, "Shop.slnx"), string.Empty);
        Directory.CreateDirectory(Path.Combine(folder.Path, ".coworkee"));
        File.WriteAllText(ExistingApp.StatePath(folder.Path),
            """{ "name": "Shop", "options": { "deployWorkflow": "azure", "deployResourceGroupPrefix": "acme", "deployBranches": "main,release/*" } }""");
        ProcessStartInfo? started = null;
        var main = Studio.Create(folder, (start, _, _) =>
        {
            started = start;
            return Task.FromResult(0);
        });
        main.Generate.Output = folder.Path;
        var workflow = main.Generate.DeployWorkflow;

        workflow.Open();

        workflow.IsOpen.ShouldBeTrue();
        workflow.Prefix.ShouldBe("acme");
        workflow.Location.ShouldBe("germanywestcentral");
        workflow.Branches.ShouldBe("main, release/*");
        workflow.Teardown.ShouldBeFalse();
        workflow.IsChangedByHand.ShouldBeFalse();

        workflow.Location = "westeurope";
        workflow.Teardown = true;
        await workflow.SaveAsync();

        started!.ArgumentList.SkipWhile(a => a != "deploy").ShouldBe(
            ["deploy", "azure", "--github", "--resource-group-prefix", "acme", "--location", "westeurope", "--branches", "main", "release/*", "--teardown-on-delete", "true"]);
        started.WorkingDirectory.ShouldBe(folder.Path);
        workflow.ExitCode.ShouldBe(0);
    }

    [Fact]
    public async Task A_workflow_changed_by_hand_asks_before_it_is_overwritten_and_can_be_kept()
    {
        using var folder = new TempFolder();
        File.WriteAllText(Path.Combine(folder.Path, "Shop.slnx"), string.Empty);
        var file = Path.Combine(folder.Path, DeployWorkflowSettings.RelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, DeployWorkflowSettings.Stamp("name: Deploy to Azure\n") + "# mine\n");
        var runs = new List<ProcessStartInfo>();
        var main = Studio.Create(folder, (start, _, _) =>
        {
            runs.Add(start);
            return Task.FromResult(0);
        });
        main.Generate.Output = folder.Path;
        var workflow = main.Generate.DeployWorkflow;
        workflow.Open();
        workflow.IsChangedByHand.ShouldBeTrue();

        await workflow.SaveAsync();

        workflow.AskOverwrite.ShouldBeTrue();
        runs.ShouldBeEmpty();
        workflow.Keep();
        workflow.IsOpen.ShouldBeFalse();
        runs.ShouldBeEmpty();

        workflow.Open();
        await workflow.SaveAsync();
        await workflow.OverwriteAsync();

        runs.Single().ArgumentList.Last().ShouldBe("--force");
        workflow.AskOverwrite.ShouldBeFalse();
        workflow.IsChangedByHand.ShouldBeFalse();
    }
}
