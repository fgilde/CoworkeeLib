using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using Coworkee.Contracts.Localization;

namespace Coworkee.Localization.MachineTranslation;

/// <summary>
/// MyMemory, a free public service without an account: 5,000 characters a day per address, 50,000 with a contact email.
/// It translates one text of up to 500 bytes per request; when the day's quota is used up the rest waits for the next run.
/// </summary>
internal sealed class MyMemoryTranslator(IHttpClientFactory clients) : ITranslationProvider
{
    public const string Endpoint = "https://api.mymemory.translated.net";
    private const int MaxBytes = 500;

    public string Name => TranslationProviders.MyMemory;

    public (int Texts, int Characters) BatchLimit => (1, MaxBytes);

    public Task<bool> IsConfiguredAsync(TranslationConnection connection, CancellationToken cancellationToken) => Task.FromResult(true);

    public async Task<IReadOnlyList<string?>> TranslateAsync(IReadOnlyList<string> texts, string from, string to, TranslationConnection connection, CancellationToken cancellationToken)
    {
        var endpoint = (connection.Endpoint is { Length: > 0 } custom ? custom : Endpoint).TrimEnd('/');
        var client = clients.CreateClient(TextTranslator.HttpClientName);
        var result = new List<string?>();
        foreach (var text in texts)
        {
            if (Encoding.UTF8.GetByteCount(text) > MaxBytes)
            {
                result.Add(null);
                continue;
            }

            var email = connection.Email is { Length: > 0 } address ? $"&de={Uri.EscapeDataString(address)}" : string.Empty;
            var answer = await client.GetFromJsonAsync<Answer>(
                $"{endpoint}/get?q={Uri.EscapeDataString(text)}&langpair={Code(from)}|{Code(to)}{email}", cancellationToken);
            if (answer is null || answer.QuotaFinished || answer.Status is not 200)
            {
                throw new HttpRequestException($"MyMemory answered {answer?.Status}: the free quota of the day is used up.", null, HttpStatusCode.TooManyRequests);
            }

            result.Add(answer.Data.TranslatedText);
        }

        return result;
    }

    internal static string Code(string culture) => TranslationTexts.Language(culture) switch
    {
        "zh" => TranslationTexts.IsTraditionalChinese(culture) ? "zh-TW" : "zh-CN",
        var language => language,
    };

    private sealed record Answer(
        [property: JsonPropertyName("responseData")] Data Data,
        [property: JsonPropertyName("responseStatus")] int Status,
        [property: JsonPropertyName("quotaFinished")] bool QuotaFinished);

    private sealed record Data([property: JsonPropertyName("translatedText")] string? TranslatedText);
}
