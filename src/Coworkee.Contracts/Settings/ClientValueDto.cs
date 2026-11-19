namespace Coworkee.Contracts.Settings;

/// <summary>
/// A client value as the caller sees it: his own value (<paramref name="IsUserValue"/>), else the organisation's global value, else no value
/// (<paramref name="Value"/> null).
/// </summary>
public sealed record ClientValueDto(string Key, string? Value, bool IsUserValue);

/// <summary>Whose client value: the caller's own, or the organisation's for every user (<see cref="SettingsPermissions.ManageGlobalClientValues"/>).</summary>
public enum ClientValueScope
{
    User,
    Global,
}

/// <summary>Saves a client value; <paramref name="Value"/> is stored as it is (usually JSON).</summary>
public sealed record SetClientValueRequest(string Key, string Value, ClientValueScope Scope = ClientValueScope.User);
