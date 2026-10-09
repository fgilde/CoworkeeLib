using System.CommandLine;

namespace Coworkee.Cli.Commands;

internal static class MigrationsCommand
{
    private static readonly Argument<string> Name = Names.Simple("name", "Name of the migration, for example AddOrders.");

    public static Command Create()
    {
        var add = new Command("add", "Add an EF Core migration to the infrastructure project.") { Name }
            .Handle((result, cancellationToken) =>
                Shell.RunAsync(AddArgs(Solution.Find(Environment.CurrentDirectory), result.GetValue(Name)!), cancellationToken: cancellationToken));
        return new Command("migrations", "Manage database migrations.") { add };
    }

    public static string[] AddArgs(Solution solution, string name)
    {
        var infrastructure = solution.Project("Infrastructure");
        return ["dotnet", "ef", "migrations", "add", name, "--project", infrastructure, "--startup-project", infrastructure];
    }
}
