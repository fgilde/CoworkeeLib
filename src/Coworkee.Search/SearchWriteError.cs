namespace Coworkee.Search;

/// <summary>A document the index refused, with the reason given by the index.</summary>
public sealed record SearchWriteError(string Id, string Reason);
