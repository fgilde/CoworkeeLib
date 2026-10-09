using System.CommandLine;
using System.CommandLine.Parsing;
using System.Text.RegularExpressions;

namespace Coworkee.Cli;

internal static partial class Names
{
    public static Argument<string> Simple(string name, string description)
    {
        var argument = new Argument<string>(name) { Description = description };
        argument.Validators.Add(Validate);
        return argument;
    }

    private static void Validate(ArgumentResult result)
    {
        var value = result.Tokens.SingleOrDefault()?.Value;
        if (value is not null && !SimpleName().IsMatch(value))
        {
            result.AddError($"'{value}' is not a simple name: start with a letter, then letters or digits only.");
        }
    }

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9]*$")]
    private static partial Regex SimpleName();
}
