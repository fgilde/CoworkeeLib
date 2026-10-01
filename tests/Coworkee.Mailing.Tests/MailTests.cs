using System.Net;
using System.Net.Http.Json;
using Coworkee.Application.Messaging;
using Coworkee.Contracts;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Mailing;
using Coworkee.Contracts.Settings;
using Coworkee.Core.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Mailing.Tests;

public sealed class MailTests(MailApp app) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync() => _setup = await app.SetupAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Admin => app.As(_setup.AdminUserId, _setup.TenantId);

    private static string Recipient() => $"{Guid.NewGuid():N}@allowed.test";

    [Fact]
    public async Task Core_template_renders_with_model_inside_the_layout()
    {
        var mail = await RenderAsync("Identity.Welcome", new { user = new { first_name = "Ada", email = "ada@acme.test" }, login_url = "https://app.test" }, "en");

        mail.HtmlBody.ShouldContain("Ada");
        mail.HtmlBody.ShouldContain("<html");
    }

    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    public async Task Every_template_renders_its_sample_model(string culture)
    {
        var definitions = app.App.Services.GetRequiredService<IMailTemplateDefinitionManager>().All;

        foreach (var definition in definitions)
        {
            var mail = await RenderAsync(definition.Name, definition.SampleModel, culture);
            mail.HtmlBody.ShouldNotBeNullOrWhiteSpace(definition.Name);
        }

        (await RenderAsync("Notifications.Digest", definitions.Single(d => d.Name == "Notifications.Digest").SampleModel, culture)).Subject.ShouldContain("1");
    }

    [Fact]
    public async Task Culture_falls_back_to_language_then_english()
    {
        (await RenderAsync("Test.Hello", new { user = new { first_name = "Ada" } }, "de-AT")).Subject.ShouldBe("Hallo Ada");
        (await RenderAsync("Test.Hello", new { user = new { first_name = "Ada" } }, "fr")).Subject.ShouldBe("Hi Ada");
    }

    [Fact]
    public async Task Tenant_override_wins_over_global_override_and_default()
    {
        await app.InDbAsync(async db =>
        {
            db.Add(new MailTemplateOverride { Name = "Test.Hello", Culture = "en", Subject = "Global {{ user.first_name }}", Body = "g" });
            db.Add(new MailTemplateOverride { Name = "Test.Hello", TenantId = _setup.TenantId, Culture = "en", Subject = "Tenant {{ user.first_name }}", Body = "t" });
            return await db.SaveChangesAsync(Ct);
        });

        (await RenderAsync("Test.Hello", new { user = new { first_name = "Ada" } }, "en")).Subject.ShouldBe("Tenant Ada");
        (await app.AsActorAsync(null, Guid.CreateVersion7(), sp => sp.GetRequiredService<IMailTemplateRenderer>()
            .RenderAsync("Test.Hello", new { user = new { first_name = "Ada" } }, "en", Ct))).Subject.ShouldBe("Global Ada");
    }

    [Fact]
    public async Task Model_values_are_html_escaped_in_the_body()
    {
        var mail = await RenderAsync("Test.Hello", new { user = new { first_name = "<script>x</script>" } }, "en");

        mail.HtmlBody.ShouldNotContain("<script>");
        mail.HtmlBody.ShouldContain("&lt;script&gt;");
    }

    [Fact]
    public void Syntax_error_is_reported_with_position() =>
        MailTemplateValidation.Validate("ok", "{{ if }}").ShouldContain(e => e.Contains("(1,", StringComparison.Ordinal));

    [Fact]
    public async Task Queued_mail_is_sent_after_commit()
    {
        var to = Recipient();

        (await DispatchAsync(new QueueTestMail(to))).IsSuccess.ShouldBeTrue();

        await MailApp.Eventually(async () => (await app.MessagesToAsync(to)).Count == 1);
        await MailApp.Eventually(async () => await StatusAsync(to) == OutgoingMailStatus.Sent);
    }

    [Fact]
    public async Task Concurrent_runs_for_the_same_mail_send_it_once()
    {
        var to = Recipient();
        var id = await app.InDbAsync(async db =>
        {
            var mail = OutgoingMail.Queue(to, new RenderedMail("Once", "<p>once</p>"), "Test.Hello", _setup.TenantId, DateTimeOffset.UtcNow);
            mail.ClearDomainEvents();
            db.Add(mail);
            await db.SaveChangesAsync(Ct);
            return mail.Id;
        });

        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => app.AsActorAsync(null, _setup.TenantId, async sp =>
        {
            await ActivatorUtilities.CreateInstance<SendMailJob>(sp).ExecuteAsync(id, Ct);
            return 0;
        })));

        await Task.Delay(TimeSpan.FromSeconds(2), Ct);
        (await app.MessagesToAsync(to)).Count.ShouldBe(1);
        (await StatusAsync(to)).ShouldBe(OutgoingMailStatus.Sent);
    }

    [Fact]
    public async Task Rolled_back_mail_is_never_sent()
    {
        var to = Recipient();

        (await DispatchAsync(new QueueTestMail(to, Fail: true))).IsSuccess.ShouldBeFalse();

        await Task.Delay(TimeSpan.FromSeconds(4), Ct);
        (await app.MessagesToAsync(to)).ShouldBeEmpty();
        (await app.InDbAsync(db => db.Set<OutgoingMail>().AnyAsync(m => m.To == to, Ct))).ShouldBeFalse();
    }

    [Fact]
    public async Task Whitelist_skips_other_recipients()
    {
        await PutGlobalAsync("Mail.Whitelist", "@allowed.test");
        var to = $"{Guid.NewGuid():N}@other.test";

        await DispatchAsync(new QueueTestMail(to));

        await MailApp.Eventually(async () => await StatusAsync(to) == OutgoingMailStatus.Skipped);
        (await app.MessagesToAsync(to)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Smtp_failure_marks_failed_after_retries()
    {
        await PutGlobalAsync("Mail.Smtp.Port", "1");
        var to = Recipient();

        await DispatchAsync(new QueueTestMail(to));

        await MailApp.Eventually(async () => await StatusAsync(to) == OutgoingMailStatus.Failed, TimeSpan.FromSeconds(90));
        (await app.InDbAsync(db => db.Set<OutgoingMail>().SingleAsync(m => m.To == to, Ct))).Attempts.ShouldBe(3);
    }

    [Fact]
    public async Task Override_with_syntax_error_is_rejected() =>
        (await Admin.PutAsJsonAsync("/api/v1/mail/templates/Test.Hello/en", new SaveMailTemplateRequest("ok", "{{ if }}"), Ct))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

    [Fact]
    public async Task Override_is_used_and_can_be_removed()
    {
        (await Admin.PutAsJsonAsync("/api/v1/mail/templates/Test.Hello/en", new SaveMailTemplateRequest("Custom", "<b>custom</b>"), Ct)).EnsureSuccessStatusCode();
        (await Admin.GetFromJsonAsync<MailTemplateDto>("/api/v1/mail/templates/Test.Hello/en", Ct))!.ShouldSatisfyAllConditions(
            t => t.IsOverridden.ShouldBeTrue(), t => t.Subject.ShouldBe("Custom"), t => t.DefaultSubject.ShouldBe("Hi {{ user.first_name }}"));

        (await Admin.DeleteAsync("/api/v1/mail/templates/Test.Hello/en", Ct)).EnsureSuccessStatusCode();

        (await Admin.GetFromJsonAsync<MailTemplateDto>("/api/v1/mail/templates/Test.Hello/en", Ct))!.IsOverridden.ShouldBeFalse();
    }

    [Fact]
    public async Task Preview_renders_the_sample_model()
    {
        var response = await Admin.PostAsJsonAsync("/api/v1/mail/templates/Test.Hello/en/preview", new SaveMailTemplateRequest("S {{ user.first_name }}", "B {{ user.first_name }}"), Ct);

        (await response.Content.ReadFromJsonAsync<RenderedMailDto>(Ct))!.Subject.ShouldBe("S Sample");
    }

    [Fact]
    public async Task Test_mail_goes_to_the_current_user_and_appears_in_the_log()
    {
        (await Admin.PostAsync("/api/v1/mail/templates/Test.Hello/en/test", null, Ct)).EnsureSuccessStatusCode();

        await MailApp.Eventually(async () => (await app.MessagesToAsync("admin@acme.test")).Any(m => m.Subject == "Hi Sample"));
        var log = await Admin.GetFromJsonAsync<PagedResult<OutgoingMailDto>>("/api/v1/mail/outgoing", Ct);
        log!.Items.ShouldContain(m => m.To == "admin@acme.test" && m.TemplateName == "Test.Hello");
    }

    [Fact]
    public async Task Templates_list_core_and_contributed_templates() =>
        new[] { "Layout", "Identity.ResetPassword", "Identity.ConfirmEmail", "Identity.Welcome", "Test.Hello" }
            .ShouldBeSubsetOf((await Admin.GetFromJsonAsync<MailTemplateSummaryDto[]>("/api/v1/mail/templates", Ct))!.Select(t => t.Name));

    [Fact]
    public async Task Templates_require_manage_permission()
    {
        var response = await Admin.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest("bob@acme.test", "Passw0rd!x", null, null), Ct);
        var bob = (await response.Content.ReadFromJsonAsync<UserDto>(Ct))!;

        (await app.As(bob.Id, _setup.TenantId).GetAsync("/api/v1/mail/templates", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private Task<RenderedMail> RenderAsync(string name, object model, string culture) =>
        app.AsActorAsync(_setup.AdminUserId, _setup.TenantId, sp => sp.GetRequiredService<IMailTemplateRenderer>().RenderAsync(name, model, culture, Ct));

    private Task<Result> DispatchAsync(QueueTestMail command) =>
        app.AsActorAsync(_setup.AdminUserId, _setup.TenantId, sp => sp.GetRequiredService<IDispatcher>().SendAsync(command, Ct));

    private Task<OutgoingMailStatus?> StatusAsync(string to) =>
        app.InDbAsync(db => db.Set<OutgoingMail>().Where(m => m.To == to).Select(m => (OutgoingMailStatus?)m.Status).SingleOrDefaultAsync(Ct));

    private async Task PutGlobalAsync(string name, string value) =>
        (await Admin.PutAsJsonAsync("/api/v1/settings/global", new SetSettingsRequest(new Dictionary<string, string?> { [name] = value }), Ct)).EnsureSuccessStatusCode();
}
