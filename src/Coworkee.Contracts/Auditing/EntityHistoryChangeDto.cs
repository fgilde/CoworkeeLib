namespace Coworkee.Contracts.Auditing;

/// <summary>One recorded field change; the values are JSON, the texts what a person reads (enum names, the name of a referenced record).</summary>
public sealed record EntityHistoryChangeDto(string Property, string? OldValue, string? NewValue, string? OldText, string? NewText, bool Restorable);
