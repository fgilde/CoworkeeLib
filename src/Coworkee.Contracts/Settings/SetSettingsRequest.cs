namespace Coworkee.Contracts.Settings;

public sealed record SetSettingsRequest(IReadOnlyDictionary<string, string?> Values);
