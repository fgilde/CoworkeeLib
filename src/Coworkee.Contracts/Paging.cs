namespace Coworkee.Contracts;

public sealed record PageRequest(int Page = 1, int PageSize = 25, string? Search = null);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize);
