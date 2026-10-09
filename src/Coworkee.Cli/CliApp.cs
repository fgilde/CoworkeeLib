using System.CommandLine;
using System.Reflection;
using Coworkee.Cli.Commands;

namespace Coworkee.Cli;

internal static class CliApp
{
    public static string Version { get; } =
        typeof(CliApp).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];

    public static RootCommand Build() => new("Create, run and maintain Coworkee applications.")
    {
        NewCommand.Create(),
        RunCommand.Create(),
        MigrationsCommand.Create(),
        UpdateCommand.Create(),
        DoctorCommand.Create(),
        ModuleCommand.Create(),
    };
}

internal static class CommandExtensions
{
    public static Command Handle(this Command command, Func<ParseResult, CancellationToken, Task<int>> action)
    {
        command.SetAction(async (result, cancellationToken) =>
        {
            try
            {
                return await action(result, cancellationToken);
            }
            catch (CliException exception)
            {
                Console.Error.WriteLine(exception.Message);
                return 1;
            }
        });
        return command;
    }
}
