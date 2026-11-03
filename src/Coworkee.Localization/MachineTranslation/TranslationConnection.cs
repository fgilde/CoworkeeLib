namespace Coworkee.Localization.MachineTranslation;

/// <summary>The translation settings: service (<see cref="Contracts.Localization.TranslationProviders"/>), key, Azure region, endpoint and MyMemory contact.</summary>
public sealed record TranslationConnection(string Provider, string? Key, string? Region, string? Endpoint, string? Email)
{
    // the key never reaches a log
    public override string ToString() => $"{Provider} {Endpoint}";
}
