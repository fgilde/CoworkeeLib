using System.Net;
using System.Net.Http.Json;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Mailing;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Mailing.Tests;

public sealed class MailTemplateLanguageTests(MailApp app) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync()
    {
        _setup = await app.SetupAsync();
        Default.Culture = null;
    }

    public ValueTask DisposeAsync()
    {
        Default.Culture = null;
        return ValueTask.CompletedTask;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Admin => app.As(_setup.AdminUserId, _setup.TenantId);

    private TestDefaultLanguage Default => app.App.Services.GetRequiredService<TestDefaultLanguage>();

    private Task<HttpResponseMessage> AddAsync(string name, string culture, string from, bool translate = false) =>
        Admin.PostAsJsonAsync($"/api/v1/mail/templates/{name}/{culture}", new AddMailTemplateLanguageRequest(from, translate), Ct);

    private async Task<MailTemplateSummaryDto> SummaryAsync(string name) =>
        (await Admin.GetFromJsonAsync<MailTemplateSummaryDto[]>("/api/v1/mail/templates", Ct))!.Single(t => t.Name == name);

    private Task<MailTemplateContent> ResolveAsync(string name, string culture) =>
        app.AsActorAsync(_setup.AdminUserId, _setup.TenantId, sp => sp.GetRequiredService<IMailTemplateRenderer>().ResolveAsync(name, culture, Ct));

    [Fact]
    public async Task A_language_added_later_starts_as_a_copy_is_listed_and_can_be_removed()
    {
        var response = await AddAsync("Test.Hello", "fr", "de");

        response.EnsureSuccessStatusCode();
        (await response.Content.ReadFromJsonAsync<MailTemplateLanguageAddedDto>(Ct))!.ShouldBe(new MailTemplateLanguageAddedDto("fr", "de", false));
        (await ResolveAsync("Test.Hello", "fr")).Subject.ShouldBe("Hallo {{ user.first_name }}");
        (await SummaryAsync("Test.Hello")).ShouldSatisfyAllConditions(
            s => s.Cultures.ShouldBe(["de", "en", "fr"]), s => s.OverriddenCultures.ShouldBe(["fr"]), s => s.DefaultCultures.ShouldBe(["de", "en"]));

        (await AddAsync("Test.Hello", "fr", "en")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await AddAsync("Test.Hello", "de", "en")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        (await Admin.DeleteAsync("/api/v1/mail/templates/Test.Hello/fr", Ct)).EnsureSuccessStatusCode();
        (await SummaryAsync("Test.Hello")).Cultures.ShouldBe(["de", "en"]);
    }

    [Fact]
    public async Task Translation_sends_only_the_texts_and_keeps_tags_and_variables()
    {
        (await Admin.PostAsJsonAsync("/api/v1/mail/templates", new CreateMailTemplateRequest("News.L", "News", null), Ct)).EnsureSuccessStatusCode();
        (await Admin.PutAsJsonAsync("/api/v1/mail/templates/News.L/en",
            new SaveMailTemplateRequest("Hi {{ user.first_name }}", "<p class=\"a\">Hello {{ user.first_name }},</p>{{ if user.email }}<b>Bye</b>{{ end }}"), Ct)).EnsureSuccessStatusCode();

        var added = await (await AddAsync("News.L", "de", "en", translate: true)).Content.ReadFromJsonAsync<MailTemplateLanguageAddedDto>(Ct);

        added!.Translated.ShouldBeTrue();
        (await ResolveAsync("News.L", "de")).ShouldBe(new MailTemplateContent(
            "de:Hi {{ user.first_name }}", "<p class=\"a\">de:Hello {{ user.first_name }},</p>{{ if user.email }}<b>de:Bye</b>{{ end }}"));
    }

    [Fact]
    public async Task The_last_language_of_an_own_template_stays()
    {
        (await Admin.PostAsJsonAsync("/api/v1/mail/templates", new CreateMailTemplateRequest("News.One", "News", null), Ct)).EnsureSuccessStatusCode();
        (await Admin.PutAsJsonAsync("/api/v1/mail/templates/News.One/de", new SaveMailTemplateRequest("Hallo", "<p>de</p>"), Ct)).EnsureSuccessStatusCode();

        (await Admin.DeleteAsync("/api/v1/mail/templates/News.One/de", Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_language_without_a_variant_falls_back_to_the_default_language_before_English()
    {
        (await Admin.PostAsJsonAsync("/api/v1/mail/templates", new CreateMailTemplateRequest("News.Fb", "News", null), Ct)).EnsureSuccessStatusCode();
        (await Admin.PutAsJsonAsync("/api/v1/mail/templates/News.Fb/en", new SaveMailTemplateRequest("Hello", "<p>en</p>"), Ct)).EnsureSuccessStatusCode();
        (await Admin.PutAsJsonAsync("/api/v1/mail/templates/News.Fb/de", new SaveMailTemplateRequest("Hallo", "<p>de</p>"), Ct)).EnsureSuccessStatusCode();

        (await ResolveAsync("News.Fb", "fr")).Subject.ShouldBe("Hello");
        (await ResolveAsync("Test.Hello", "fr")).Subject.ShouldBe("Hi {{ user.first_name }}");

        Default.Culture = "de";

        (await ResolveAsync("News.Fb", "fr")).Subject.ShouldBe("Hallo");
        (await ResolveAsync("Test.Hello", "fr")).Subject.ShouldBe("Hallo {{ user.first_name }}");
        (await ResolveAsync("News.Fb", "en")).Subject.ShouldBe("Hello");
    }
}
