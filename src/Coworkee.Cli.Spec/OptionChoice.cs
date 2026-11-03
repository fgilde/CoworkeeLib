namespace Coworkee.Cli.Spec;

/// <summary>One choice of a choice option; <paramref name="InfoUrl"/> is the website of the product it stands for.</summary>
public sealed record OptionChoice(string Value, string Description, string? InfoUrl = null);
