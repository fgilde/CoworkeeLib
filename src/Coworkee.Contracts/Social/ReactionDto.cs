namespace Coworkee.Contracts.Social;

/// <summary>One emoji on an entity with everyone who reacted with it, first reaction first.</summary>
public sealed record ReactionDto(string Emoji, IReadOnlyList<Guid> UserIds);
