namespace Coworkee.Contracts;

public sealed record PageRequest(int Page = 1, int PageSize = 25, string? Search = null);
