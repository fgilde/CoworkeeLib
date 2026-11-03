using Coworkee.Contracts.Settings;

namespace Coworkee.Settings;

internal sealed record StoredSetting(string Name, SettingScope Scope, string? Value);
