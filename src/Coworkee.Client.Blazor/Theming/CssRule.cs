namespace Coworkee.Client.Blazor.Theming;

/// <summary>A plain <c>selector { declarations }</c> rule; <paramref name="Declarations"/> is the trimmed text between the braces.</summary>
public sealed record CssRule(string Selector, string Declarations) : CssBlock;
