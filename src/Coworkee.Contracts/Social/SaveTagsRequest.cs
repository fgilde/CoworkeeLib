namespace Coworkee.Contracts.Social;

public sealed record SaveTagsRequest(IReadOnlyList<string> Tags);
