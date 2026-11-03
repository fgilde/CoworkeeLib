namespace Coworkee.Cli.Spec;

public enum OptionKind
{
    Bool,
    Choice,
    Text,

    /// <summary>#rrggbb.</summary>
    Color,

    /// <summary>A local file the CLI copies into the app (an http(s) address is passed as it is where noted).</summary>
    File,

    /// <summary>Several of the choices, comma separated (a JSON array in the spec).</summary>
    Multi,
}
