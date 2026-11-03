namespace Coworkee.Contracts.Settings;

/// <summary>A typed configuration section the app lets admins edit (its class is usually generated from a JSON file).</summary>
public sealed record AppConfigurationDto(string Section, string Title);
