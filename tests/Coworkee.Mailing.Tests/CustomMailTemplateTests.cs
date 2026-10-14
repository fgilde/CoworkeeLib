using System.Net;
using System.Net.Http.Json;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Mailing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Mailing.Tests;

public sealed class CustomMailTemplateTests(MailApp app) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync() => _setup = await app.SetupAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Admin => app.As(_setup.AdminUserId, _setup.TenantId);

    private Task<HttpResponseMessage> CreateAsync(HttpClient client, CreateMailTemplateRequest request) =>
        client.PostAsJsonAsync("/api/v1/mail/templates", request, Ct);

    private Task<HttpResponseMessage> SaveAsync(string name, string culture, string subject, string body) =>
        Admin.PutAsJsonAsync($"/api/v1/mail/templates/{name}/{culture}", new SaveMailTemplateRequest(subject, body), Ct);

    [Fact]
    public async Task Created_template_is_listed_as_custom_and_renders_with_the_recipient_model()
    {
        (await CreateAsync(Admin, new("News.1", "News", "Monthly news"))).EnsureSuccessStatusCode();
        (await SaveAsync("News.1", "de", "Hallo {{ user.first_name }}", "<p>{{ user.display_name }}</p>")).EnsureSuccessStatusCode();

        var listed = (await Admin.GetFromJsonAsync<MailTemplateSummaryDto[]>("/api/v1/mail/templates", Ct))!.Single(t => t.Name == "News.1");
        listed.ShouldSatisfyAllConditions(
            t => t.IsCustom.ShouldBeTrue(), t => t.Description.ShouldBe("Monthly news"), t => t.DisplayName.ShouldBe("News"), t => t.OverriddenCultures.ShouldBe(["de"]));
        var model = MailRecipientModel.For(new MailRecipient(Guid.NewGuid(), "ada@x.test", "Ada", "Lovelace", "en"), "App", "https://app.test", "Org", "https://app.test/u", null);

        var mail = await app.AsActorAsync(_setup.AdminUserId, _setup.TenantId, sp => sp.GetRequiredService<IMailTemplateRenderer>().RenderAsync("News.1", model, "en", Ct));

        mail.Subject.ShouldBe("Hallo Ada");
        mail.HtmlBody.ShouldContain("Ada Lovelace");
        (await Admin.GetFromJsonAsync<MailTemplateDto>("/api/v1/mail/templates/News.1/de", Ct))!.Variables.ShouldContain(v => v.Name == "user.first_name");
    }

    [Fact]
    public async Task Names_are_validated()
    {
        (await CreateAsync(Admin, new("Dup.1", "Dup", null))).EnsureSuccessStatusCode();

        foreach (var name in new[] { "bad name", "Identity.ResetPassword", "Dup.1", "", new string('a', 129) })
        {
            (await CreateAsync(Admin, new(name, "X", null))).StatusCode.ShouldBe(HttpStatusCode.BadRequest, name);
        }
    }

    [Fact]
    public async Task Other_tenants_do_not_see_the_template()
    {
        (await CreateAsync(Admin, new("Mine.1", "Mine", null))).EnsureSuccessStatusCode();
        var other = await app.CreateTenantAdminAsync();
        var tenantB = app.As(other.UserId, other.TenantId);
        (await CreateAsync(tenantB, new("Theirs.1", "Theirs", null))).EnsureSuccessStatusCode();

        (await tenantB.GetFromJsonAsync<MailTemplateSummaryDto[]>("/api/v1/mail/templates", Ct))!.Select(t => t.Name).ShouldSatisfyAllConditions(
            names => names.ShouldContain("Theirs.1"), names => names.ShouldNotContain("Mine.1"));
        (await tenantB.GetAsync("/api/v1/mail/templates/Mine.1/en", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await tenantB.DeleteAsync("/api/v1/mail/templates/Mine.1", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Admin.GetAsync("/api/v1/mail/templates/Mine.1/en", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Global_needs_the_system_organisation()
    {
        var other = await app.CreateTenantAdminAsync();

        (await CreateAsync(app.As(other.UserId, other.TenantId), new("Everyone.1", "Everyone", null, Global: true))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        (await CreateAsync(Admin, new("Everyone.1", "Everyone", null, Global: true))).EnsureSuccessStatusCode();
        (await SaveAsync("Everyone.1", "en", "Hi", "<p>all</p>")).EnsureSuccessStatusCode();
        var tenantB = app.As(other.UserId, other.TenantId);
        (await tenantB.GetFromJsonAsync<MailTemplateDto>("/api/v1/mail/templates/Everyone.1/en", Ct))!.Subject.ShouldBe("Hi");
        (await tenantB.PutAsJsonAsync("/api/v1/mail/templates/Everyone.1/en", new SaveMailTemplateRequest("Mine", "x"), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await tenantB.DeleteAsync("/api/v1/mail/templates/Everyone.1", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Delete_removes_the_content()
    {
        (await CreateAsync(Admin, new("Gone.1", "Gone", null))).EnsureSuccessStatusCode();
        (await SaveAsync("Gone.1", "en", "Subject", "<p>body</p>")).EnsureSuccessStatusCode();

        (await Admin.DeleteAsync("/api/v1/mail/templates/Gone.1", Ct)).EnsureSuccessStatusCode();
        (await CreateAsync(Admin, new("Gone.1", "Gone", null))).EnsureSuccessStatusCode();

        (await Admin.GetFromJsonAsync<MailTemplateDto>("/api/v1/mail/templates/Gone.1/en", Ct))!.ShouldSatisfyAllConditions(
            t => t.Subject.ShouldBeEmpty(), t => t.Body.ShouldBeEmpty(), t => t.IsOverridden.ShouldBeFalse());
        (await app.InDbAsync(db => db.Set<MailTemplateOverride>().CountAsync(o => o.Name == "Gone.1", Ct))).ShouldBe(0);
    }

    [Fact]
    public async Task Registered_templates_cannot_be_deleted() =>
        (await Admin.DeleteAsync("/api/v1/mail/templates/Identity.Welcome", Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

    [Fact]
    public async Task Copy_from_copies_all_cultures()
    {
        (await CreateAsync(Admin, new("Copy.1", "Copy", null, CopyFrom: "Identity.Welcome"))).EnsureSuccessStatusCode();

        var expected = app.App.Services.GetRequiredService<IMailTemplateDefinitionManager>().Find("Identity.Welcome")!.Defaults.Keys;
        var copied = await app.InDbAsync(db => db.Set<MailTemplateOverride>().Where(o => o.Name == "Copy.1").Select(o => o.Culture).ToListAsync(Ct));
        copied.ShouldBe(expected, ignoreOrder: true);
        (await Admin.GetFromJsonAsync<MailTemplateDto>("/api/v1/mail/templates/Copy.1/de", Ct))!.Body.ShouldContain("{{");
    }
}
