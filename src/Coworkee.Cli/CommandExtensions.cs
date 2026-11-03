using System.CommandLine;

namespace Coworkee.Cli;

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
