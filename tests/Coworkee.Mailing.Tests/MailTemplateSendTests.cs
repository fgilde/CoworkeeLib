using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Coworkee.Contracts;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Mailing;
using Coworkee.Identity.Domain;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Mailing.Tests;

/// <summary>A template sent for real: recipients from a user selection, one mail per recipient with his own values, through the mail log.</summary>
public sealed class MailTemplateSendTests(MailApp app) : IAsyncLifetime
{
    private const string Welcome = "/api/v1/mail/templates/Identity.Welcome";
    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync() => _setup = await app.SetupAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Admin => app.As(_setup.AdminUserId, _setup.TenantId);

    [Fact]
    public async Task The_template_shows_its_sample_model_as_json_with_the_names_it_reads()
    {
        var template = (await Admin.GetFromJsonAsync<MailTemplateDto>("/api/v1/mail/templates/Identity.Welcome/en", Ct))!;

        var model = JsonNode.Parse(template.SampleModel!)!.AsObject();
        model["user"]!["first_name"]!.GetValue<string>().ShouldBe("Ada");
        model["login_url"].ShouldNotBeNull();
    }

    [Fact]
    public async Task Sending_queues_one_mail_per_eligible_recipient_with_his_values_and_the_given_ones()
    {
        await SeedUserAsync("grace@seed.test", "Grace");
        await SeedUserAsync("linus@seed.test", "Linus");
        await SeedUserAsync("off@seed.test", "Off", active: false);
        var request = new SendMailTemplateRequest(new UserSelection("endswith(Email,'@seed.test')"), """{ "user": { "first_name": "x" }, "login_url": "https://custom.test/in" }""");

        var preview = await ReadAsync<MailTemplateSendPreviewDto>(await Admin.PostAsJsonAsync($"{Welcome}/send/preview", request, Ct));
        (preview.Recipients, preview.Excluded).ShouldBe((2, 1));
        preview.Mail!.HtmlBody.ShouldContain("https://custom.test/in");

        (await ReadAsync<int>(await Admin.PostAsJsonAsync($"{Welcome}/send", request, Ct))).ShouldBe(2);

        var mails = await app.InDbAsync(db => db.Set<OutgoingMail>().AsNoTracking().Where(m => m.TemplateName == "Identity.Welcome").ToListAsync(Ct));
        mails.Select(m => m.To).ShouldBe(["grace@seed.test", "linus@seed.test"], ignoreOrder: true);
        mails.Single(m => m.To == "grace@seed.test").Subject.ShouldBe("Welcome, Grace");
        mails.Single(m => m.To == "linus@seed.test").HtmlBody.ShouldSatisfyAllConditions(b => b.ShouldContain("Hello Linus"), b => b.ShouldContain("https://custom.test/in"));
        mails.ShouldAllBe(m => m.CampaignId == null);
    }

    [Fact]
    public async Task The_send_model_asks_only_for_the_values_the_template_reads_and_never_for_the_recipients_own()
    {
        var template = (await Admin.GetFromJsonAsync<MailTemplateDto>($"{Welcome}/en", Ct))!;

        var model = JsonNode.Parse(template.SendModel!)!.AsObject();
        model.ContainsKey("user").ShouldBeFalse();
        model.ContainsKey("unsubscribe_url").ShouldBeFalse();
        model.Select(p => p.Key).ShouldAllBe(key => template.Body.Contains(key) || template.Subject.Contains(key));
    }

    [Fact]
    public async Task A_send_with_only_the_asked_values_still_greets_each_recipient_by_name()
    {
        await SeedUserAsync("grace@seed.test", "Grace");
        var template = (await Admin.GetFromJsonAsync<MailTemplateDto>($"{Welcome}/en", Ct))!;

        (await ReadAsync<int>(await Admin.PostAsJsonAsync($"{Welcome}/send", new SendMailTemplateRequest(new UserSelection("Email eq 'grace@seed.test'"), template.SendModel), Ct))).ShouldBe(1);

        var mail = await app.InDbAsync(db => db.Set<OutgoingMail>().AsNoTracking().SingleAsync(m => m.To == "grace@seed.test", Ct));
        mail.Subject.ShouldBe("Welcome, Grace");
        mail.HtmlBody.ShouldContain("Hello Grace");
    }

    [Fact]
    public async Task The_preview_pages_through_the_recipients_in_send_order()
    {
        await SeedUserAsync("grace@seed.test", "Grace");
        await SeedUserAsync("linus@seed.test", "Linus");
        var request = new SendMailTemplateRequest(new UserSelection("endswith(Email,'@seed.test')"));

        var shown = new List<string?>();
        foreach (var index in new[] { 0, 1, 7 })
        {
            var preview = await ReadAsync<MailTemplateSendPreviewDto>(await Admin.PostAsJsonAsync($"{Welcome}/send/preview?index={index}", request, Ct));
            preview.Recipients.ShouldBe(2);
            preview.Mail!.Subject.ShouldBe($"Welcome, {preview.Recipient!.Split(' ')[0]}");
            shown.Add(preview.Recipient);
        }

        shown[0].ShouldNotBe(shown[1]);
        shown[2].ShouldBe(shown[1]); // past the end: the last one
        shown.ShouldAllBe(r => r!.EndsWith("@seed.test>"));
        (await Admin.PostAsJsonAsync($"{Welcome}/send/preview?index=-1", request, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Sending_refuses_the_layout_unknown_templates_bad_models_and_other_organisations_users()
    {
        var bob = await SeedUserAsync("bob@seed.test", "Bob");
        var (otherAdmin, otherTenant) = await app.CreateTenantAdminAsync();
        var selection = UserSelection.Of(bob);

        (await Admin.PostAsJsonAsync("/api/v1/mail/templates/Layout/send", new SendMailTemplateRequest(selection), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Admin.PostAsJsonAsync("/api/v1/mail/templates/Nope/send", new SendMailTemplateRequest(selection), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Admin.PostAsJsonAsync($"{Welcome}/send", new SendMailTemplateRequest(selection, "[1,2]"), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Admin.PostAsJsonAsync($"{Welcome}/send", new SendMailTemplateRequest(selection, "{ nope"), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Admin.PostAsJsonAsync($"{Welcome}/send", new SendMailTemplateRequest(new UserSelection("PasswordHash eq 'x'")), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // the admin of another organisation picks bob by id: nobody of his own
        (await ReadAsync<int>(await app.As(otherAdmin, otherTenant).PostAsJsonAsync($"{Welcome}/send", new SendMailTemplateRequest(selection), Ct))).ShouldBe(0);
        (await app.InDbAsync(db => db.Set<OutgoingMail>().IgnoreQueryFilters().CountAsync(m => m.To == "bob@seed.test", Ct))).ShouldBe(0);
    }

    [Fact]
    public async Task Sending_needs_template_management_campaigns_and_the_right_to_view_users()
    {
        var bob = await SeedUserAsync("bob@seed.test", "Bob");
        var request = new SendMailTemplateRequest(UserSelection.Of(bob));
        var editor = await UserWithAsync("editor@seed.test", MailPermissions.Templates.Manage);
        var mailer = await UserWithAsync("mailer@seed.test", MailPermissions.Templates.Manage, MailPermissions.Campaigns.Send);

        (await editor.PostAsJsonAsync($"{Welcome}/send", request, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await editor.PostAsJsonAsync($"{Welcome}/send/preview", request, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await mailer.PostAsJsonAsync($"{Welcome}/send", request, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden); // no Users.View
        (await app.InDbAsync(db => db.Set<OutgoingMail>().CountAsync(m => m.To == "bob@seed.test", Ct))).ShouldBe(0);
    }

    private async Task<HttpClient> UserWithAsync(string email, params string[] permissions)
    {
        var user = await ReadAsync<UserDto>(await Admin.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest(email, "Passw0rd!x", null, null), Ct));
        (await Admin.PutAsJsonAsync($"/api/v1/identity/permissions/grants/User/{user.Id}", new NameListRequest(permissions), Ct)).EnsureSuccessStatusCode();
        return app.As(user.Id, _setup.TenantId);
    }

    private Task<Guid> SeedUserAsync(string email, string firstName, bool active = true) => app.InDbAsync(async db =>
    {
        var user = new User
        {
            TenantId = _setup.TenantId, UserName = email, NormalizedUserName = email.ToUpperInvariant(), Email = email, NormalizedEmail = email.ToUpperInvariant(),
            EmailConfirmed = true, FirstName = firstName, IsActive = active,
        };
        db.Add(user);
        await db.SaveChangesAsync(Ct);
        return user.Id;
    });

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>(Ct))!;
    }
}
