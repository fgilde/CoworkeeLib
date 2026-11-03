namespace Coworkee.Contracts.Social;

public sealed record EntityTagsDto(IReadOnlyList<string> Tags, bool CanEdit);
