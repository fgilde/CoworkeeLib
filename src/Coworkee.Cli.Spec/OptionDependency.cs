namespace Coworkee.Cli.Spec;

/// <summary>
/// An option that only matters while another one has one of <paramref name="Values"/> (none: while it is set, a bool being true),
/// or while <see cref="Or"/> is met.
/// </summary>
public sealed record OptionDependency(string Option, params string[] Values)
{
    public OptionDependency? Or { get; init; }

    public bool IsMet(string? value) => Values.Length == 0 ? value is not (null or "" or "false") : value is not null && Values.Contains(value);

    public override string ToString() => (Values.Length == 0 ? Option : $"{Option} = {string.Join(" or ", Values)}") + (Or is { } other ? $", or {other}" : string.Empty);
}
