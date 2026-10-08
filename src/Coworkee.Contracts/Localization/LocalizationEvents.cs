namespace Coworkee.Contracts.Localization;

/// <summary>Languages or texts changed; every client reloads its texts and leaves a language that was switched off.</summary>
public static class LocalizationEvents
{
    public const string Topic = "global:localization";

    public const string Changed = "localization.changed";
}
