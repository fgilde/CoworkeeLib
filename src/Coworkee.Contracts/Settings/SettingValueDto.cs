namespace Coworkee.Contracts.Settings;

public sealed record SettingValueDto(string Name, string? Value, bool HasValue);
