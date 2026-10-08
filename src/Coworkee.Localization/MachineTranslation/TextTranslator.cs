using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Coworkee.Contracts.Localization;
using Coworkee.Settings;

namespace Coworkee.Localization.MachineTranslation;

/// <summary>Machine translation of English texts with the Azure AI Translator, configured by the localization settings.</summary>
public sealed partial class TextTranslator(IHttpClientFactory clients, ISettingProvider settings)
{
    public const string HttpClientName = "Coworkee.Translator";
    private const int BatchSize = 100;
    private const int BatchCharacters = 40_000;

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken) =>
        !string.IsNullOrWhiteSpace(await settings.GetAsync(LocalizationSettings.TranslatorKey, cancellationToken));

    /// <returns>English text to translation; texts the service did not return are left out.</returns>
    public async Task<IReadOnlyDictionary<string, string>> TranslateAsync(IReadOnlyCollection<string> texts, string culture, CancellationToken cancellationToken)
    {
        var key = await settings.GetAsync(LocalizationSettings.TranslatorKey, cancellationToken);
        if (string.IsNullOrWhiteSpace(key) || texts.Count == 0)
        {
            return new Dictionary<string, string>();
        }

        var region = await settings.GetAsync(LocalizationSettings.TranslatorRegion, cancellationToken);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var batch in Batches(texts))
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"translate?api-version=3.0&from=en&to={Uri.EscapeDataString(culture)}&textType=html")
            {
                Content = JsonContent.Create(batch.Select(t => new TranslatorText(Protect(t))).ToList()),
            };
            request.Headers.Add("Ocp-Apim-Subscription-Key", key);
            if (!string.IsNullOrWhiteSpace(region))
            {
                request.Headers.Add("Ocp-Apim-Subscription-Region", region);
            }

            using var response = await clients.CreateClient(HttpClientName).SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            var answers = await response.Content.ReadFromJsonAsync<List<TranslatorAnswer>>(cancellationToken) ?? [];
            foreach (var (text, answer) in batch.Zip(answers))
            {
                if (answer.Translations.FirstOrDefault()?.Text is { Length: > 0 } translated)
                {
                    result[text] = Unprotect(translated);
                }
            }
        }

        return result;
    }

    private static IEnumerable<List<string>> Batches(IEnumerable<string> texts)
    {
        var batch = new List<string>();
        var characters = 0;
        foreach (var text in texts)
        {
            if (batch.Count == BatchSize || characters + text.Length > BatchCharacters)
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

    // placeholders like {0} must survive the translation unchanged
    private static string Protect(string text) => Placeholder().Replace(System.Net.WebUtility.HtmlEncode(text), m => $"<span class=\"notranslate\">{m.Value}</span>");

    private static string Unprotect(string text) => System.Net.WebUtility.HtmlDecode(Wrapper().Replace(text, "$1"));

    [GeneratedRegex(@"\{\d+(:[^}]*)?\}")]
    private static partial Regex Placeholder();

    [GeneratedRegex(@"<span class=""notranslate"">(.*?)</span>")]
    private static partial Regex Wrapper();

    private sealed record TranslatorText([property: JsonPropertyName("Text")] string Text);

    private sealed record TranslatorAnswer([property: JsonPropertyName("translations")] List<TranslatorTranslation> Translations);

    private sealed record TranslatorTranslation([property: JsonPropertyName("text")] string Text);
}
