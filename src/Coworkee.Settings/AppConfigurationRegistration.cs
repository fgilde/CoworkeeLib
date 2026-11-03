namespace Coworkee.Settings;

/// <summary>
/// A typed section admins may edit; the type binds the section (usually generated from a JSON file with Nextended.CodeGen).
/// <see cref="Locked"/>, <see cref="Protected"/> and <see cref="Hidden"/> are property paths below the section; locked values show but never change, protected ones
/// change only after the form unlocks them (the server saves them), hidden ones never leave the server.
/// </summary>
public sealed record AppConfigurationRegistration(string Section, string Title, Type Type)
{
    public IReadOnlyList<string> Locked { get; init; } = [];

    public IReadOnlyList<string> Protected { get; init; } = [];

    public IReadOnlyList<string> Hidden { get; init; } = [];

    /// <summary>The app settings (one per app, see AddCoworkeeSettings); the other sections come next to it.</summary>
    public bool IsAppSettings { get; init; }
}
