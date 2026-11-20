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
