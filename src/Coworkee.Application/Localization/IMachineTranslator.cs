namespace Coworkee.Application.Localization;

/// <summary>Machine translation with the service of the translation settings; the localization module provides it.</summary>
public interface IMachineTranslator
{
    /// <returns>Text to translation; texts the service did not translate, or whose {0} placeholders it broke, are left out. Empty without a configured service.</returns>
    Task<IReadOnlyDictionary<string, string>> TranslateAsync(IReadOnlyCollection<string> texts, string from, string to, CancellationToken cancellationToken);
}
