using System.Text.RegularExpressions;

namespace Coworkee.Cli.Spec.Import;

/// <summary>C# identifiers from table, column, schema and file names.</summary>
public static partial class ImportNames
{
    /// <summary>order_items, ORDER-ITEMS and orderItems become OrderItems; a leading digit gets an N.</summary>
    public static string Pascal(string raw)
    {
        var name = string.Concat(Words().Matches(raw).Select(m => m.Value).Select(w =>
            char.ToUpperInvariant(w[0]) + (w.Length > 1 && w.All(char.IsUpper) ? w[1..].ToLowerInvariant() : w[1..])));
        return name.Length == 0 ? "Unnamed" : char.IsDigit(name[0]) ? "N" + name : name;
    }

    // ponytail: naive English singular, enough for table names like Orders, Categories, Addresses
    public static string Singular(string name) => name switch
    {
        [.., 'i', 'e', 's'] when name.Length > 4 => name[..^3] + "y",
        [.., 's', 's', 'e', 's'] or [.., 'x', 'e', 's'] or [.., 'c', 'h', 'e', 's'] or [.., 's', 'h', 'e', 's'] or [.., 'u', 's', 'e', 's'] => name[..^2],
        [.., 's', 's'] or [.., 'u', 's'] or [.., 'i', 's'] => name,
        [_, _, .., 's'] => name[..^1],
        _ => name,
    };

    /// <summary>An entity or enum name that is no type the generated code uses and not yet in <paramref name="taken"/> (which gets it).</summary>
    public static string TypeName(string name, ISet<string> taken) =>
        Unique(SpecValidator.ReservedTypes.Contains(name, StringComparer.OrdinalIgnoreCase) ? name + "Item" : name, taken);

    public static string Unique(string name, ISet<string> taken)
    {
        var unique = Enumerable.Range(1, 999).Select(i => i == 1 ? name : name + i).First(n => !taken.Contains(n));
        taken.Add(unique);
        return unique;
    }

    /// <summary>A property name free on the entity; a reference also needs its Id and Reference members.</summary>
    public static string Member(string name, string entity, ISet<string> members, bool reference = false)
    {
        if (IsBase(name) || name.Equals(entity, StringComparison.OrdinalIgnoreCase))
        {
            name += "Value";
        }

        var unique = Enumerable.Range(1, 999).Select(i => i == 1 ? name : name + i)
            .First(n => !members.Contains(n) && (!reference || (!members.Contains(n + "Id") && !members.Contains(n + "Reference"))));
        members.Add(unique);
        if (reference)
        {
            members.Add(unique + "Id");
            members.Add(unique + "Reference");
        }

        return unique;
    }

    public static bool IsBase(string name) => SpecValidator.BaseProperties.Contains(name, StringComparer.OrdinalIgnoreCase);

    public static ISet<string> Set() => new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    [GeneratedRegex("[A-Z]+(?![a-z])|[A-Z]?[a-z]+|[0-9]+")]
    private static partial Regex Words();
}
