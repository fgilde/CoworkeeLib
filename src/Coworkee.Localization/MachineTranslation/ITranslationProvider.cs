namespace Coworkee.Localization.MachineTranslation;

/// <summary>A machine translation service, one per value of <see cref="Contracts.Localization.TranslationProviders"/>; the setting picks it by <see cref="Name"/>.</summary>
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
