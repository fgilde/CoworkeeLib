namespace Coworkee.Localization.MachineTranslation;

/// <summary>The translation settings: service (<see cref="Contracts.Localization.TranslationProviders"/>), key, Azure region, endpoint and MyMemory contact.</summary>
public sealed record TranslationConnection(string Provider, string? Key, string? Region, string? Endpoint, string? Email)
{
    // the key never reaches a log
    public override string ToString() => $"{Provider} {Endpoint}";
}

/// <summary>A machine translation service. Register more with <c>AddScoped&lt;ITranslationProvider, ...&gt;()</c>; the setting names it by <see cref="Name"/>.</summary>
public interface ITranslationProvider
{
    string Name { get; }

    /// <summary>Texts and characters per request.</summary>
    (int Texts, int Characters) BatchLimit { get; }

    Task<bool> IsConfiguredAsync(TranslationConnection connection, CancellationToken cancellationToken);

    /// <summary>Translates from culture <paramref name="from"/> to <paramref name="to"/>.</summary>
    /// <returns>One translation per text, null where the service returned none.</returns>
    Task<IReadOnlyList<string?>> TranslateAsync(IReadOnlyList<string> texts, string from, string to, TranslationConnection connection, CancellationToken cancellationToken);
}
