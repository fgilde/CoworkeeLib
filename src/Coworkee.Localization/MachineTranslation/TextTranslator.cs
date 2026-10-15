using Coworkee.Contracts.Localization;
using Coworkee.Settings;
using Microsoft.Extensions.Logging;

namespace Coworkee.Localization.MachineTranslation;

/// <summary>Machine translation with the service of the translation settings (<see cref="ITranslationProvider"/>), in batches the service accepts.</summary>
public sealed class TextTranslator(IEnumerable<ITranslationProvider> providers, ISettingProvider settings, ILogger<TextTranslator> logger) : Application.Localization.IMachineTranslator
{
    public const string HttpClientName = "Coworkee.Translator";

    public async Task<TranslationConnection> ConnectionAsync(CancellationToken cancellationToken) => new(
        await settings.GetAsync(LocalizationSettings.TranslationProvider, cancellationToken) ?? TranslationProviders.None,
        await settings.GetAsync(LocalizationSettings.TranslatorKey, cancellationToken),
        await settings.GetAsync(LocalizationSettings.TranslatorRegion, cancellationToken),
        await settings.GetAsync(LocalizationSettings.TranslationEndpoint, cancellationToken),
        await settings.GetAsync(LocalizationSettings.TranslationEmail, cancellationToken));

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken)
    {
        var connection = await ConnectionAsync(cancellationToken);
        return Provider(connection) is { } provider && await provider.IsConfiguredAsync(connection, cancellationToken);
    }

    /// <returns>Text to translation; texts the service did not translate, or whose placeholders it broke, are left out.</returns>
    /// <remarks>A failing service ends the run with what was translated so far (e.g. a used up free quota); the rest waits for the next run.</remarks>
    public async Task<IReadOnlyDictionary<string, string>> TranslateAsync(IReadOnlyCollection<string> texts, string from, string to, CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var connection = await ConnectionAsync(cancellationToken);
        if (texts.Count == 0 || Provider(connection) is not { } provider || !await provider.IsConfiguredAsync(connection, cancellationToken))
        {
            return result;
        }

        foreach (var batch in Batches(texts, provider.BatchLimit))
        {
            try
            {
                var answers = await provider.TranslateAsync(batch, from, to, connection, cancellationToken);
                foreach (var (text, answer) in batch.Zip(answers))
                {
                    if (answer is { Length: > 0 } translated && TranslationTexts.KeepsPlaceholders(text, translated))
                    {
                        result[text] = translated;
                    }
                }
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException && !cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(exception, "Translation to {Culture} with {Connection} stopped after {Count} texts.", to, connection, result.Count);
                break;
            }
        }

        return result;
    }

    private ITranslationProvider? Provider(TranslationConnection connection) =>
        providers.FirstOrDefault(p => string.Equals(p.Name, connection.Provider, StringComparison.OrdinalIgnoreCase));

    internal static IEnumerable<List<string>> Batches(IEnumerable<string> texts, (int Texts, int Characters) limit)
    {
        var batch = new List<string>();
        var characters = 0;
        foreach (var text in texts)
        {
            if (batch.Count > 0 && (batch.Count == limit.Texts || characters + text.Length > limit.Characters))
            {
                yield return batch;
                (batch, characters) = ([], 0);
            }

            batch.Add(text);
            characters += text.Length;
        }

        if (batch.Count > 0)
        {
            yield return batch;
        }
    }
}
