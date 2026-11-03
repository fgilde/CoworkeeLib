using Coworkee.Contracts.Search;

namespace Coworkee.Application.GlobalSearch;

public sealed record SearchMatchResult(double Score, IReadOnlyList<HighlightRange> Highlights);
