namespace Coworkee.Studio.Cli;

public enum CliLineKind
{
    Text,
    Step,
    Command,
    Warning,
    Error,
    NextSteps,
}

/// <summary>One line the CLI printed: <c>==&gt; step</c>, <c>&gt; command</c>, <c>warn  text</c>, <c>Next steps:</c> or plain output.</summary>
public sealed record CliLine(CliLineKind Kind, string Text)
{
    public static CliLine Parse(string line, bool error = false) => line switch
    {
        _ when error => new(CliLineKind.Error, line),
        ['=', '=', '>', ' ', .. var step] => new(CliLineKind.Step, step),
        ['>', ' ', .. var command] => new(CliLineKind.Command, command),
        _ when line.StartsWith("warn  ", StringComparison.Ordinal) => new(CliLineKind.Warning, line[6..]),
        "Next steps:" => new(CliLineKind.NextSteps, line),
        _ => new(CliLineKind.Text, line),
    };
}
