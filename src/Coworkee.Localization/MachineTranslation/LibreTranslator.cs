using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Coworkee.Contracts.Localization;

namespace Coworkee.Localization.MachineTranslation;

/// <summary>LibreTranslate, open source and self-hosted (e.g. the container of the app host): free, no account, the texts stay in-house.</summary>
internal sealed class LibreTranslator(IHttpClientFactory clients) : ITranslationProvider
{
    public string Name => TranslationProviders.LibreTranslate;

    // ponytail: small batches keep a request to the CPU-bound server well inside the client timeout
    public (int Texts, int Characters) BatchLimit => (25, 5_000);

    public Task<bool> IsConfiguredAsync(TranslationConnection connection, CancellationToken cancellationToken) =>
        Task.FromResult(Uri.TryCreate(connection.Endpoint, UriKind.Absolute, out _));

    public async Task<IReadOnlyList<string?>> TranslateAsync(IReadOnlyList<string> texts, string from, string to, TranslationConnection connection, CancellationToken cancellationToken)
    {
        var body = new Body([.. texts.Select(TranslationTexts.ToHtml)], Code(from), Code(to), "html", connection.Key is { Length: > 0 } key ? key : null);
        using var response = await clients.CreateClient(TextTranslator.HttpClientName)
            .PostAsJsonAsync($"{connection.Endpoint!.TrimEnd('/')}/translate", body, cancellationToken);
        response.EnsureSuccessStatusCode();
        var answer = await response.Content.ReadFromJsonAsync<Answer>(cancellationToken);
        return [.. (answer?.TranslatedText ?? []).Select(t => (string?)TranslationTexts.FromHtml(t))];
    }

    internal static string Code(string culture) => TranslationTexts.Language(culture) switch
    {
        "zh" => TranslationTexts.IsTraditionalChinese(culture) ? "zh-Hant" : "zh-Hans",
        var language => language,
    };

    private sealed record Body(
        [property: JsonPropertyName("q")] string[] Q,
        [property: JsonPropertyName("source")] string Source,
        [property: JsonPropertyName("target")] string Target,
        [property: JsonPropertyName("format")] string Format,
        [property: JsonPropertyName("api_key"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ApiKey);

    private sealed record Answer([property: JsonPropertyName("translatedText")] List<string> TranslatedText);
}
