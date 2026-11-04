namespace Coworkee.Contracts.Ai;

/// <summary>A page as the user sees it in the navigation; <see cref="Group"/> is the menu path above it, e.g. "Administration > System".</summary>
public sealed record AiUiPage(string Title, string Href, string? Group = null, string? Description = null);
