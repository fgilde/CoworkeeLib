using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Coworkee.Contracts.Localization;

namespace Coworkee.Localization.MachineTranslation;

/// <summary>DeepL: keys ending in ":fx" belong to the free plan (500,000 characters a month) and its own host.</summary>
internal sealed class DeepLTranslator(IHttpClientFactory clients) : ITranslationProvider
{
    public const string FreeEndpoint = "https://api-free.deepl.com";
    public const string ProEndpoint = "https://api.deepl.com";

    public string Name => TranslationProviders.DeepL;

    // a request may carry 128 KiB
    public (int Texts, int Characters) BatchLimit => (50, 30_000);

    public Task<bool> IsConfiguredAsync(TranslationConnection connection, CancellationToken cancellationToken) =>
        Task.FromResult(!string.IsNullOrWhiteSpace(connection.Key));

    public async Task<IReadOnlyList<string?>> TranslateAsync(IReadOnlyList<string> texts, string from, string to, TranslationConnection connection, CancellationToken cancellationToken)
    {
        var body = new Body([.. texts.Select(TranslationTexts.ToXml)], TranslationTexts.Language(from).ToUpperInvariant(), Target(to), "xml", ["x"]);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{EndpointOf(connection)}/v2/translate") { Content = JsonContent.Create(body) };
        request.Headers.TryAddWithoutValidation("Authorization", $"DeepL-Auth-Key {connection.Key}");
        using var response = await clients.CreateClient(TextTranslator.HttpClientName).SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var answer = await response.Content.ReadFromJsonAsync<Answer>(cancellationToken);
        return [.. (answer?.Translations ?? []).Select(t => (string?)TranslationTexts.FromXml(t.Text))];
    }

    internal static string EndpointOf(TranslationConnection connection) =>
        connection.Endpoint is { Length: > 0 } custom ? custom.TrimEnd('/')
        : connection.Key?.EndsWith(":fx", StringComparison.Ordinal) == true ? FreeEndpoint : ProEndpoint;

    internal static string Target(string culture) => TranslationTexts.Language(culture) switch
    {
        "en" => culture.Equals("en-US", StringComparison.OrdinalIgnoreCase) ? "EN-US" : "EN-GB",
        "pt" => culture.Equals("pt-BR", StringComparison.OrdinalIgnoreCase) ? "PT-BR" : "PT-PT",
        "zh" => TranslationTexts.IsTraditionalChinese(culture) ? "ZH-HANT" : "ZH-HANS",
        var language => language.ToUpperInvariant(),
    };

    private sealed record Body(
        [property: JsonPropertyName("text")] string[] Text,
        [property: JsonPropertyName("source_lang")] string SourceLang,
        [property: JsonPropertyName("target_lang")] string TargetLang,
        [property: JsonPropertyName("tag_handling")] string TagHandling,
        [property: JsonPropertyName("ignore_tags")] string[] IgnoreTags);

    private sealed record Answer([property: JsonPropertyName("translations")] List<Translation> Translations);

    private sealed record Translation([property: JsonPropertyName("text")] string Text);
}
