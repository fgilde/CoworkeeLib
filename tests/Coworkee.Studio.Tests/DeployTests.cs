using System.Diagnostics;
using Coworkee.Studio.ViewModels;

namespace Coworkee.Studio.Tests;

public sealed class DeployTests
{
    [Fact]
    public async Task Deploy_runs_coworkee_deploy_for_the_target_in_the_app_folder_and_shows_its_output()
    {
        using var folder = new TempFolder();
        ProcessStartInfo? started = null;
        var main = Studio.Create(folder, (start, output, _) =>
        {
            started = start;
            output("Addresses (the reverse proxy forwards each public address to its loopback port):", false);
            return Task.FromResult(0);
        });
        main.Generate.Output = folder.Path;
        var deploy = main.Generate.Deploy;
        deploy.Open();
        deploy.Target = "k8s";

        await deploy.StartAsync();

        deploy.IsOpen.ShouldBeTrue();
        started!.ArgumentList.TakeLast(2).ShouldBe(["deploy", "k8s"]);
        started.WorkingDirectory.ShouldBe(folder.Path);
        deploy.Log.ShouldContain(l => l.StartsWith("Addresses", StringComparison.Ordinal));
        deploy.ExitCode.ShouldBe(0);
        deploy.IsBusy.ShouldBeFalse();
        DeployViewModel.Targets.ShouldBe(["compose", "azure", "k8s"]);
    }
}
