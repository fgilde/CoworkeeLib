namespace Coworkee.Contracts.Identity;

public sealed record IdListRequest(IReadOnlyList<Guid> Ids);
