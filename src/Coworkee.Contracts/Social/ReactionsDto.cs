namespace Coworkee.Contracts.Social;

public sealed record ReactionsDto(IReadOnlyList<ReactionDto> Reactions, bool CanReact);
