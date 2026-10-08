namespace Coworkee.Client;

public sealed record ODataResult<T>(IReadOnlyList<T> Items, long? Count);
