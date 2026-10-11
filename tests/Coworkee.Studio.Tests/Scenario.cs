using Coworkee.Cli.Spec;
using Coworkee.Studio.Cli;
using Coworkee.Studio.ViewModels;

namespace Coworkee.Studio.Tests;

/// <summary>The shop the screenshots and UI tests show: a business app with a Sales module.</summary>
internal static class Scenario
{
    public static MainViewModel Shop(TempFolder folder, string? location = null)
    {
        var main = Studio.Create(folder);
        main.NewApp();
        main.Basics.Name = "Shop";
        main.Basics.Location = location ?? folder.Path;
        main.Option("title").Value = "Acme Shop";
        main.Option("theme").Value = "Forest";
        main.Branding.UseBrandColors = true;
        main.Option("primaryColor").Value = "#0a7d5a";
        main.Option("secondaryColor").Value = "#ff8f00";
        main.Architecture.ApplyPreset(main.Architecture.Presets.Single(p => p.Preset.Name == "Business app"));
        main.Entities.AddExample();
        main.Users.AddRole();
        var role = main.Users.Roles[0];
        role.Name = "Sales";
        role.Description = "Takes orders";
        foreach (var permission in role.Permissions.Where(p => p.Name.StartsWith("Sales.", StringComparison.Ordinal) && !p.Name.EndsWith(".Delete", StringComparison.Ordinal)))
        {
            permission.IsChecked = true;
        }

        main.Users.AddUser();
        main.Users.Users[0].Email = "ada@shop.test";
        main.Users.Users[0].DisplayName = "Ada Lovelace";
        foreach (var choice in main.Users.Users[0].Roles)
        {
            choice.IsChecked = choice.Name == "Sales";
        }
        main.Users.AddUser();
        main.Users.Users[1].Email = "grace@shop.test";
        main.Users.Users[1].Admin = true;
        foreach (var step in main.Steps)
        {
            step.IsReachable = true;
        }

        main.UpdateStates();
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
            string.Empty, "Next steps:", "  cd " + output, "  coworkee run",
        })
        {
            generate.Add(CliLine.Parse(line));
        }

        generate.Elapsed = "1:42";
        generate.ShowLog = true;
        generate.Finish(GenerationOutcome.Succeeded);
        generate.SignIn = new SignInInfo("Shop",
        [
            new SignInUser("admin@shop.local", "Cw1!3f9c2a7e41b84d0c9e6a15b2d8f7c403", true, []),
            new SignInUser("ada@shop.test", "Cw1!8b1e5d0c27f94a6e9d3c4b7a1f2e6d58", false, ["Sales"]),
            new SignInUser("grace@shop.test", "Cw1!c47a9e2b6d1f4038a5e9b7c2d6f1a3e0", true, []),
        ], Keycloak: true);
    }
}
