using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Coworkee.Contracts.Localization;

namespace Coworkee.Localization.MachineTranslation;

/// <summary>Google Cloud Translation (Basic, v2) with an API key; 500,000 characters a month are free.</summary>
internal sealed class GoogleTranslator(IHttpClientFactory clients) : ITranslationProvider
{
    public const string Endpoint = "https://translation.googleapis.com";

    public string Name => TranslationProviders.Google;

    public (int Texts, int Characters) BatchLimit => (128, 20_000);

    public Task<bool> IsConfiguredAsync(TranslationConnection connection, CancellationToken cancellationToken) =>
        Task.FromResult(!string.IsNullOrWhiteSpace(connection.Key));

    public async Task<IReadOnlyList<string?>> TranslateAsync(IReadOnlyList<string> texts, string from, string to, TranslationConnection connection, CancellationToken cancellationToken)
    {
        var endpoint = (connection.Endpoint is { Length: > 0 } custom ? custom : Endpoint).TrimEnd('/');
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{endpoint}/language/translate/v2")
        {
            Content = JsonContent.Create(new Body([.. texts.Select(TranslationTexts.ToHtml)], Code(from), Code(to), "html")),
        };
        request.Headers.Add("X-Goog-Api-Key", connection.Key);
        using var response = await clients.CreateClient(TextTranslator.HttpClientName).SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var answer = await response.Content.ReadFromJsonAsync<Answer>(cancellationToken);
        return [.. (answer?.Data.Translations ?? []).Select(t => (string?)TranslationTexts.FromHtml(t.TranslatedText))];
    }

    internal static string Code(string culture) => TranslationTexts.Language(culture) switch
    {
        "zh" => TranslationTexts.IsTraditionalChinese(culture) ? "zh-TW" : "zh-CN",
        var language => language,
    };

    private sealed record Body(
        [property: JsonPropertyName("q")] string[] Q,
        [property: JsonPropertyName("source")] string Source,
        [property: JsonPropertyName("target")] string Target,
        [property: JsonPropertyName("format")] string Format);

    private sealed record Answer([property: JsonPropertyName("data")] Data Data);

    private sealed record Data([property: JsonPropertyName("translations")] List<Translation> Translations);

    private sealed record Translation([property: JsonPropertyName("translatedText")] string TranslatedText);
}
