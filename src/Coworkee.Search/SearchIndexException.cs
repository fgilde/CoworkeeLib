namespace Coworkee.Search;

/// <summary>The index refused a request; <see cref="IsInvalidRequest"/> when the request itself was at fault (bad value, bad cursor), otherwise the index is unavailable.</summary>
public sealed class SearchIndexException(string message, bool isInvalidRequest) : Exception(message)
{
    public bool IsInvalidRequest { get; } = isInvalidRequest;
}
