namespace Coworkee.Cli.Spec;

/// <summary>
/// One switch of <c>coworkee new</c>. <see cref="TemplateSymbol"/> options go to <c>dotnet new coworkee</c> as <c>--{Id}</c>;
/// the others are handled by the CLI itself. <see cref="InfoUrl"/> links to more about what the option brings;
/// <see cref="DependsOn"/> names the option it needs: without it this one is neither asked for nor used.
/// </summary>
public sealed record CliOption(
    string Id,
    string Group,
    string Title,
    string Description,
    OptionKind Kind,
    string? Default,
    string WhenToUse,
    IReadOnlyList<string> Pros,
    IReadOnlyList<string> Cons,
    IReadOnlyList<OptionChoice>? Choices = null,
    string? Package = null,
    bool TemplateSymbol = true,
    string? InfoUrl = null,
    OptionDependency? DependsOn = null)
{
    /// <summary>The flag of <c>coworkee new</c>: the id in kebab case, e.g. <c>--event-bus</c>.</summary>
    public string Flag => "--" + string.Concat(Id.Select((c, i) => char.IsUpper(c) ? (i > 0 && char.IsLower(Id[i - 1]) ? "-" : "") + char.ToLowerInvariant(c) : c.ToString()));
}
