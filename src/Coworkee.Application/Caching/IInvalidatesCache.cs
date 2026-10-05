namespace Coworkee.Application.Caching;

public interface IInvalidatesCache
{
    IReadOnlyList<string> CacheTags { get; }
}
