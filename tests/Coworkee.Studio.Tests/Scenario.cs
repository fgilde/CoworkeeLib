using Coworkee.Studio.Cli;
using Coworkee.Studio.ViewModels;

namespace Coworkee.Studio.Tests;

/// <summary>The shop the screenshots and UI tests show: a business app with a Sales module.</summary>
internal static class Scenario
{
    public static MainViewModel Shop(TempFolder folder)
    {
        var main = Studio.Create(folder);
        main.NewApp();
        main.Basics.Name = "Shop";
        main.Option("title").Value = "Acme Shop";
        main.Architecture.ApplyPreset(main.Architecture.Presets.Single(p => p.Preset.Name == "Business app"));
        main.Entities.AddExample();
        foreach (var step in main.Steps)
        {
            step.IsReachable = true;
        }

        return main;
    }

    public static void Generated(GenerateStep generate, string output)
    {
        generate.Output = output;
        generate.RunState = RunState.Running;
        foreach (var line in new[]
        {
            "> coworkee spec validate shop.json", "ok", "> coworkee new --spec shop.json -o " + output,
            "==> Installing the template", "> dotnet new install Coworkee.Templates@0.1.0 --force", "Success: Coworkee.Templates::0.1.0 installed the following templates:",
            "==> Creating the solution", "> dotnet new coworkee -n Shop -o " + output, "The template \"Coworkee application\" was created successfully.",
            "==> Generating module Sales", "==> Restoring packages", "> dotnet restore", "  Restore complete (12.4s)",
            "==> Adding the migration AddSales", "> dotnet ef migrations add AddSales", "Done. To undo this action, use 'ef migrations remove'",
            string.Empty, "Next steps:", "  cd " + output, "  coworkee run", "Sign in as admin@shop.local, the password is in src/Shop.Migrations/DemoSeed.cs.",
        })
        {
            generate.Add(CliLine.Parse(line));
        }

        generate.Elapsed = "1:42";
        generate.Finish(GenerationOutcome.Succeeded);
    }
}
