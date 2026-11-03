namespace Coworkee.Contracts.Settings;

/// <summary>The head script as it applies to the caller: of the organisation, else of the installation.</summary>
public sealed record HeadScriptDto(bool Enabled, string Value);
