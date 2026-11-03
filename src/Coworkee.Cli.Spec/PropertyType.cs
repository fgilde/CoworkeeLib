namespace Coworkee.Cli.Spec;

public enum PropertyType
{
    String,
    Int,
    Long,
    Decimal,
    Double,
    Bool,
    DateTime,
    Guid,
    DateOnly,
    TimeOnly,

    /// <summary>Long text without maximum length, several lines in forms.</summary>
    Text,
    Email,
    Url,

    /// <summary>A decimal with precision 18 and scale 2 unless given.</summary>
    Money,
    Enum,
    Reference,
}
