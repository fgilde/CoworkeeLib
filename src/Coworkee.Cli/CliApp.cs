using System.CommandLine;
using System.CommandLine.Help;
using System.CommandLine.Invocation;
using System.Reflection;
using Coworkee.Cli.Commands;

namespace Coworkee.Cli;

internal static class CliApp
{
    public static string Version { get; } =
        typeof(CliApp).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];

    public const string Description = """
        Create, run and maintain Coworkee applications.

        Start with coworkee options (every switch of coworkee new) or coworkee spec example (an app as JSON for coworkee new --spec).
        coworkee ui opens Coworkee Studio, a desktop wizard for the same. coworkee mcp is an MCP server for AI agents:
          claude mcp add coworkee -- coworkee mcp
        """;

    public static RootCommand Build()
    {
        var root = new RootCommand(Description)
        {
            NewCommand.Create(),
            OptionsCommand.Create(),
            SpecCommand.Create(),
            SpecsCommand.Create(),
            RunCommand.Create(),
            MigrationsCommand.Create(),
            ModuleCommand.Create(),
            EntityCommand.Create(),
            TextsCommand.Create(),
            UpdateCommand.Create(),
            SelfUpdateCommand.Create(),
            DoctorCommand.Create(),
            UiCommand.Create(),
            McpCommand.Create(),
        };
        var help = root.Options.OfType<HelpOption>().Single();
        help.Action = new ExtendedHelp((SynchronousCommandLineAction)help.Action!);
        return root;
    }

    /// <summary>The help of coworkee mcp ends with how to register the server.</summary>
    private sealed class ExtendedHelp(SynchronousCommandLineAction help) : SynchronousCommandLineAction
    {
        public override int Invoke(ParseResult parseResult)
        {
            var exit = help.Invoke(parseResult);
            if (parseResult.CommandResult.Command.Name == "mcp")
            {
                Console.WriteLine(McpCommand.Help);
            }

            return exit;
        }
    }
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
