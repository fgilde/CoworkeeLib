namespace Coworkee.Theming;

/// <summary>
/// A built-in theme (<paramref name="BasedOn"/>) with other palette colors, e.g. <c>["Primary"] = "#0a7d5a"</c>.
/// <paramref name="Dark"/> defaults to the light colors; a changed Primary or Secondary gets a readable contrast text unless one is given.
/// </summary>
public sealed record AppTheme(string Name, string BasedOn, IReadOnlyDictionary<string, string> Light, IReadOnlyDictionary<string, string>? Dark = null);
