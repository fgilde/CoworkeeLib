namespace Coworkee.Contracts.Settings;

/// <summary>Sets the head script of the caller's organisation, or of the installation when the caller is in the system organisation.</summary>
public sealed record SetHeadScriptRequest(bool Enabled, string? Value);
