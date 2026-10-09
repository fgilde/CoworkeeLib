using System.CommandLine;

namespace Coworkee.Cli.Commands;

internal static class RunCommand
{
    public static Command Create() => new Command("run", "Run the Aspire app host of the solution in the current folder.")
        .Handle((_, cancellationToken) => Shell.RunAsync(Args(Solution.Find(Environment.CurrentDirectory)), cancellationToken: cancellationToken));

    public static string[] Args(Solution solution) => ["dotnet", "run", "--project", solution.Project("AppHost")];
}
