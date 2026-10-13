using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Coworkee.Contracts.Localization;
using Coworkee.Localization.MachineTranslation;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Localization.Tests;

public sealed class TranslationProviderTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static TranslationConnection Connection(string provider, string? key = "secret", string? endpoint = null, string? email = null) =>
        new(provider, key, "westeurope", endpoint, email);

    [Fact]
    public async Task Azure_sends_html_with_protected_placeholders_and_the_key_headers()
    {
        var http = new RecordingHandler("""[{"translations":[{"text":"<span class=\"notranslate\" translate=\"no\">{0}</span> gespeichert &amp; fertig"}]}]""");

        var result = await new AzureTranslator(http.Factory).TranslateAsync(["{0} saved & done"], "en", "zh-TW", Connection(TranslationProviders.Azure), Ct);

        result.ShouldBe(["{0} gespeichert & fertig"]);
        http.Request.RequestUri!.ToString().ShouldBe("https://api.cognitive.microsofttranslator.com/translate?api-version=3.0&from=en&to=zh-Hant&textType=html");
        http.Request.Headers.GetValues("Ocp-Apim-Subscription-Key").ShouldBe(["secret"]);
        http.Request.Headers.GetValues("Ocp-Apim-Subscription-Region").ShouldBe(["westeurope"]);
        JsonNode.Parse(http.Body)![0]!["Text"]!.GetValue<string>().ShouldBe("<span class=\"notranslate\" translate=\"no\">{0}</span> saved &amp; done");
    }

    [Fact]
    public async Task DeepL_picks_the_free_host_for_fx_keys_and_keeps_placeholders_in_ignored_tags()
    {
        var http = new RecordingHandler("""{"translations":[{"text":"<x>{0}</x> Dateien"}]}""");

        var result = await new DeepLTranslator(http.Factory).TranslateAsync(["{0} files"], "de", "pt-BR", Connection(TranslationProviders.DeepL, "abc:fx"), Ct);

        result.ShouldBe(["{0} Dateien"]);
        http.Request.RequestUri!.ToString().ShouldBe("https://api-free.deepl.com/v2/translate");
        http.Request.Headers.Authorization!.ToString().ShouldBe("DeepL-Auth-Key abc:fx");
        var body = JsonNode.Parse(http.Body)!;
        (body["source_lang"]!.GetValue<string>(), body["target_lang"]!.GetValue<string>(), body["tag_handling"]!.GetValue<string>()).ShouldBe(("DE", "PT-BR", "xml"));
        body["text"]![0]!.GetValue<string>().ShouldBe("<x>{0}</x> files");
        body["ignore_tags"]![0]!.GetValue<string>().ShouldBe("x");
        DeepLTranslator.EndpointOf(Connection(TranslationProviders.DeepL, "pro-key")).ShouldBe(DeepLTranslator.ProEndpoint);
    }

    [Fact]
    public async Task Google_sends_the_texts_as_html_with_the_key_header()
    {
        var http = new RecordingHandler("""{"data":{"translations":[{"translatedText":"Bonjour"},{"translatedText":"Au revoir"}]}}""");

        var result = await new GoogleTranslator(http.Factory).TranslateAsync(["Hello", "Bye"], "en", "fr-CH", Connection(TranslationProviders.Google), Ct);

        result.ShouldBe(["Bonjour", "Au revoir"]);
        http.Request.RequestUri!.ToString().ShouldBe("https://translation.googleapis.com/language/translate/v2");
        http.Request.Headers.GetValues("X-Goog-Api-Key").ShouldBe(["secret"]);
        var body = JsonNode.Parse(http.Body)!;
        (body["source"]!.GetValue<string>(), body["target"]!.GetValue<string>(), body["format"]!.GetValue<string>()).ShouldBe(("en", "fr", "html"));
    }

    [Fact]
    public async Task LibreTranslate_posts_to_the_configured_server_and_needs_no_key()
    {
        var http = new RecordingHandler("""{"translatedText":["Speichern"]}""");
        var connection = Connection(TranslationProviders.LibreTranslate, key: null, endpoint: "http://libre:5000/");
        var translator = new LibreTranslator(http.Factory);

        var result = await translator.TranslateAsync(["Save"], "en", "de-AT", connection, Ct);

        result.ShouldBe(["Speichern"]);
        http.Request.RequestUri!.ToString().ShouldBe("http://libre:5000/translate");
        var body = JsonNode.Parse(http.Body)!;
        (body["source"]!.GetValue<string>(), body["target"]!.GetValue<string>(), body["format"]!.GetValue<string>()).ShouldBe(("en", "de", "text"));
        body.AsObject().ContainsKey("api_key").ShouldBeFalse();
        (await translator.IsConfiguredAsync(connection with { Endpoint = null }, Ct)).ShouldBeFalse();
    }

    [Fact]
    public async Task MyMemory_translates_one_text_per_request_with_the_contact_email_and_skips_long_texts()
    {
        var http = new RecordingHandler("""{"responseData":{"translatedText":"Hallo"},"responseStatus":200,"quotaFinished":false}""");

        var result = await new MyMemoryTranslator(http.Factory)
            .TranslateAsync(["Hello", new string('a', 600)], "en", "de", Connection(TranslationProviders.MyMemory, key: null, email: "admin@acme.test"), Ct);

        result.ShouldBe(["Hallo", null]);
        http.Count.ShouldBe(1);
        http.Request.RequestUri!.OriginalString.ShouldBe("https://api.mymemory.translated.net/get?q=Hello&langpair=en|de&de=admin%40acme.test");
    }

    [Fact]
    public async Task A_used_up_quota_ends_the_run_and_keeps_what_was_translated()
    {
        var answers = new Queue<string>([
            """{"responseData":{"translatedText":"Eins"},"responseStatus":200,"quotaFinished":false}""",
            """{"responseData":{"translatedText":"MYMEMORY WARNING: YOU USED ALL AVAILABLE FREE TRANSLATIONS FOR TODAY"},"responseStatus":429,"quotaFinished":true}""",
        ]);
        var http = new RecordingHandler(answers.Dequeue);
        var translator = Facade(new MyMemoryTranslator(http.Factory), TranslationProviders.MyMemory);

        var result = await translator.TranslateAsync(["One", "Two", "Three"], "en", "de", Ct);

        result.ShouldBe(new Dictionary<string, string> { ["One"] = "Eins" });
        http.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Translations_that_lose_a_placeholder_are_dropped()
    {
        var http = new RecordingHandler("""{"translatedText":["{0} Dateien","Dateien"]}""");
        var translator = Facade(new LibreTranslator(http.Factory), TranslationProviders.LibreTranslate, "http://libre");

        var result = await translator.TranslateAsync(["{0} files", "{0} of {1} files"], "en", "de", Ct);

        result.ShouldBe(new Dictionary<string, string> { ["{0} files"] = "{0} Dateien" });
    }

    [Fact]
    public void Batches_respect_the_text_and_character_limits_of_a_service()
    {
        TextTranslator.Batches(["aaaa", "bbbb", "cc", "d", "e"], (3, 8)).Select(b => string.Join(',', b)).ShouldBe(["aaaa,bbbb", "cc,d,e"]);
        TextTranslator.Batches([new string('x', 20), "y"], (10, 8)).Select(b => b.Count).ShouldBe([1, 1]);
    }

    [Fact]
    public void The_ai_answer_is_read_as_a_json_array_of_the_same_length()
    {
        AiTranslator.Parse("```json\n[\"Eins\", \"{0} Zwei\"]\n```", 2).ShouldBe(["Eins", "{0} Zwei"]);
        AiTranslator.Parse("[\"Eins\"]", 2).ShouldBe([null, null]);
        AiTranslator.Parse("Sorry, I cannot.", 1).ShouldBe([null]);
    }

    [Fact]
    public async Task The_ai_service_is_not_available_without_the_ai_module()
    {
        var services = new ServiceCollection().BuildServiceProvider();

        (await new AiTranslator(services).IsConfiguredAsync(Connection(TranslationProviders.Ai), Ct)).ShouldBeFalse();
    }

    private static TextTranslator Facade(ITranslationProvider provider, string name, string? endpoint = null) =>
        new([provider], new FixedSettings(new Dictionary<string, string?>
        {
            [LocalizationSettings.TranslationProvider] = name,
            [LocalizationSettings.TranslationEndpoint] = endpoint,
        }), Microsoft.Extensions.Logging.Abstractions.NullLogger<TextTranslator>.Instance);

    internal sealed class FixedSettings(IReadOnlyDictionary<string, string?> values) : Coworkee.Settings.ISettingProvider
    {
        public Task<string?> GetAsync(string name, CancellationToken cancellationToken = default) => Task.FromResult(values.GetValueOrDefault(name));

        public Task<T> GetAsync<T>(string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<string?> GetForScopeAsync(string name, Coworkee.Contracts.Settings.SettingScope scope, bool storedOnly = false, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyDictionary<string, string?>> GetClientValuesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    /// <summary>Answers every request with the given JSON and keeps the last request and its body.</summary>
    private sealed class RecordingHandler(Func<string> answer) : HttpMessageHandler, IHttpClientFactory
    {
        public RecordingHandler(string answer)
            : this(() => answer)
        {
        }

        public HttpRequestMessage Request { get; private set; } = null!;

        public string Body { get; private set; } = string.Empty;

        public int Count { get; private set; }

        public IHttpClientFactory Factory => this;

        public HttpClient CreateClient(string name) => new(this, disposeHandler: false);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            (Request, Count) = (request, Count + 1);
            Body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(answer(), Encoding.UTF8, "application/json") };
        }
    }
}
