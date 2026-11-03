namespace Coworkee.Cli.Spec;

public enum MappingStrategy
{
    /// <summary>A hand-written ToDto extension method, like the template's samples.</summary>
    Manual,

    /// <summary>A Mapperly mapper generated at compile time.</summary>
    Mapperly,
}
