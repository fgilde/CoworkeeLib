namespace Coworkee.Contracts.Localization;

/// <summary>Keys a client asked for and found no text for, so administrators can translate them.</summary>
public sealed record MissingTextsRequest(IReadOnlyList<string> Keys);
