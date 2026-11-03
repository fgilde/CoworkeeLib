namespace Coworkee.Contracts.Auditing;

/// <summary><see cref="ChangedLater"/>: an undo replaces a value a later entry wrote.</summary>
public sealed record EntityHistoryFieldDto(string Property, string? CurrentText, string? RestoredText, bool ChangedLater = false);
