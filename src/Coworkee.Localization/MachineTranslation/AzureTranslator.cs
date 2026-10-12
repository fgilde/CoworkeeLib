using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Coworkee.Contracts.Localization;

namespace Coworkee.Localization.MachineTranslation;

/// <summary>Azure AI Translator: key and region of a Translator resource; the free tier covers 2 million characters a month.</summary>
internal sealed class AzureTranslator(IHttpClientFactory clients) : ITranslationProvider
{
    public const string Endpoint = "https://api.cognitive.microsofttranslator.com";

    public string Name => TranslationProviders.Azure;

    public (int Texts, int Characters) BatchLimit => (100, 40_000);

    public Task<bool> IsConfiguredAsync(TranslationConnection connection, CancellationToken cancellationToken) =>
        Task.FromResult(!string.IsNullOrWhiteSpace(connection.Key));

    public async Task<IReadOnlyList<string?>> TranslateAsync(IReadOnlyList<string> texts, string from, string to, TranslationConnection connection, CancellationToken cancellationToken)
    {
        var endpoint = (connection.Endpoint is { Length: > 0 } custom ? custom : Endpoint).TrimEnd('/');
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{endpoint}/translate?api-version=3.0&from={Code(from)}&to={Code(to)}&textType=html")
        {
            Content = JsonContent.Create(texts.Select(t => new Text(TranslationTexts.ToHtml(t))).ToList()),
        };
        request.Headers.Add("Ocp-Apim-Subscription-Key", connection.Key);
        if (!string.IsNullOrWhiteSpace(connection.Region))
        {
            request.Headers.Add("Ocp-Apim-Subscription-Region", connection.Region);
        }

        using var response = await clients.CreateClient(TextTranslator.HttpClientName).SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var answers = await response.Content.ReadFromJsonAsync<List<Answer>>(cancellationToken) ?? [];
        return [.. answers.Select(a => a.Translations.FirstOrDefault()?.Text is { } text ? TranslationTexts.FromHtml(text) : null)];
    }

    internal static string Code(string culture) => TranslationTexts.Language(culture) switch
    {
        "zh" => TranslationTexts.IsTraditionalChinese(culture) ? "zh-Hant" : "zh-Hans",
        "pt" when culture.Equals("pt-PT", StringComparison.OrdinalIgnoreCase) => "pt-pt",
        "fr" when culture.Equals("fr-CA", StringComparison.OrdinalIgnoreCase) => "fr-ca",
        var language => language,
    };

    private sealed record Text([property: JsonPropertyName("Text")] string Value);

    private sealed record Answer([property: JsonPropertyName("translations")] List<Translation> Translations);

    private sealed record Translation([property: JsonPropertyName("text")] string Text);
}
