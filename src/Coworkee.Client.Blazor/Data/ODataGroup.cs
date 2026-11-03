namespace Coworkee.Client.Blazor.Data;

/// <summary>One group of <see cref="IODataClient.GroupAsync{T}"/>: the values of the grouping properties, in their order and types, and the rows it holds.</summary>
public sealed record ODataGroup(IReadOnlyList<object?> Values, long Count);
