using System.Net.Http.Json;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Mailing;
using Coworkee.Identity.Domain;
using Coworkee.Mailing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Coworkee.Notifications.Tests;

public sealed class NotificationMailTemplateTests(NotificationApp app) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;
    private Guid _bob;

    public async ValueTask InitializeAsync()
    {
        TestNotificationModule.MilestoneDelays.Clear();
        _setup = await app.SetupAsync();
        var admin = app.As(_setup.AdminUserId, _setup.TenantId);
        _bob = (await (await admin.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest("bob@acme.test", "Passw0rd!x", null, null), Ct))
            .Content.ReadFromJsonAsync<UserDto>(Ct))!.Id;
    }

    public ValueTask DisposeAsync()
    {
        TestNotificationModule.MilestoneDelays.Clear();
        return ValueTask.CompletedTask;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_type_with_a_template_mails_it_with_the_model_next_to_user_and_notification()
    {
        TestNotificationModule.MilestoneDelays[_setup.TenantId] = TimeSpan.Zero;

        await app.PublishAsync(_setup.AdminUserId, _setup.TenantId, Milestone("1 May", "2 May", "<b>late</b>") with { Recipients = [_bob] });

        var mail = (await MailsToAsync("bob@acme.test")).ShouldHaveSingleItem();
        (mail.TemplateName, mail.Subject, mail.TenantId).ShouldBe((TestNotificationModule.MilestoneTemplate, "S-1: ETA", _setup.TenantId));
        mail.HtmlBody.ShouldContain("bob@acme.test|1 May|2 May|&lt;b&gt;late&lt;/b&gt;|Departed|https://app.test/shipments/1");
        (await app.InDbAsync(null, db => db.Set<PendingNotificationMail>().CountAsync(Ct))).ShouldBe(0);
    }

    [Fact]
    public async Task Without_a_template_or_with_one_that_is_gone_the_generic_mail_goes_out()
    {
        await app.PublishAsync(_setup.AdminUserId, _setup.TenantId, Milestone("1 May", "2 May") with { Type = TestNotificationModule.Missing, Recipients = [_bob] });
        await app.PublishAsync(_setup.AdminUserId, _setup.TenantId, Milestone("1 May", "2 May") with { Type = TestNotificationModule.Eta, Recipients = [_bob] });
        await app.RunMailJobAsync(new FakeTimeProvider(DateTimeOffset.UtcNow.AddMinutes(2)));

        var mails = await MailsToAsync("bob@acme.test");
        mails.Count.ShouldBe(2);
        mails.ShouldAllBe(m => m.TemplateName == NotificationMails.Template && m.Subject == "Departed");
        mails.ShouldAllBe(m => m.HtmlBody.Contains("https://app.test/shipments/1"));
    }

    [Fact]
    public async Task A_newer_notification_of_the_entity_replaces_the_model_of_the_waiting_mail()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var shipment = Guid.CreateVersion7();
        var entity = new Func<NotificationMessage, NotificationMessage>(m => m with { EntityType = "Shipments", EntityId = shipment, Recipients = [_bob] });

        await app.PublishAsync(_setup.AdminUserId, _setup.TenantId, entity(Milestone("1 May", "2 May")), clock);
        clock.Advance(TimeSpan.FromSeconds(30));
        await app.PublishAsync(_setup.AdminUserId, _setup.TenantId, entity(Milestone("2 May", "9 May")), clock);
        (await app.InDbAsync(null, db => db.Set<PendingNotificationMail>().SingleAsync(Ct))).MailModel!.ShouldContain("9 May");

        clock.Advance(TimeSpan.FromMinutes(1));
        await app.RunMailJobAsync(clock);

        var mail = (await MailsToAsync("bob@acme.test")).ShouldHaveSingleItem();
        mail.Subject.ShouldBe("S-1: ETA");
        mail.HtmlBody.ShouldContain("|2 May|9 May|");
    }

    [Fact]
    public async Task The_mail_delay_is_the_sending_tenants_and_delayed_mails_render_in_their_tenant()
    {
        var (other, dora) = await AddTenantWithUserAsync("dora@other.test");
        TestNotificationModule.MilestoneDelays[_setup.TenantId] = TimeSpan.FromMinutes(5);
        await app.InDbAsync(null, async db =>
        {
            db.Add(new MailTemplateOverride { Name = TestNotificationModule.MilestoneTemplate, TenantId = other, Culture = "en", Subject = "Other: {{ shipment.number }}", Body = "<p>{{ new_value }}</p>" });
            return await db.SaveChangesAsync(Ct);
        });
        var now = DateTimeOffset.UtcNow;

        await app.PublishAsync(_setup.AdminUserId, _setup.TenantId, Milestone("1 May", "2 May") with { Recipients = [_bob] }, new FakeTimeProvider(now));
        await app.PublishAsync(null, other, Milestone("1 May", "3 May") with { Recipients = [dora] }, new FakeTimeProvider(now));

        await app.RunMailJobAsync(new FakeTimeProvider(now.AddMinutes(2)));
        var doras = (await MailsToAsync("dora@other.test")).ShouldHaveSingleItem();
        (doras.Subject, doras.TenantId).ShouldBe(("Other: S-1", other));
        doras.HtmlBody.ShouldContain("3 May");
        (await MailsToAsync("bob@acme.test")).ShouldBeEmpty();

        await app.RunMailJobAsync(new FakeTimeProvider(now.AddMinutes(6)));
        var bobs = (await MailsToAsync("bob@acme.test")).ShouldHaveSingleItem();
        (bobs.Subject, bobs.TenantId).ShouldBe(("S-1: ETA", _setup.TenantId));
    }

    [Fact]
    public async Task Preview_and_test_mail_of_the_template_use_its_sample_model()
    {
        var admin = app.As(_setup.AdminUserId, _setup.TenantId);
        var url = $"/api/v1/mail/templates/{TestNotificationModule.MilestoneTemplate}/en";

        var preview = await (await admin.PostAsJsonAsync($"{url}/preview",
            new SaveMailTemplateRequest("{{ shipment.number }} {{ milestone_name }}", "{{ user.first_name }}|{{ old_value }}|{{ notification.title }}"), Ct))
            .Content.ReadFromJsonAsync<RenderedMailDto>(Ct);
        preview!.Subject.ShouldBe("S-9 ETA");
        preview.HtmlBody.ShouldContain("Ada|1 May|Document shared");

        (await admin.PostAsync($"{url}/test", null, Ct)).EnsureSuccessStatusCode();
        (await MailsToAsync("admin@acme.test")).ShouldHaveSingleItem().HtmlBody.ShouldContain("ada@example.com|1 May|2 May|On time|Document shared");
    }

    [Fact]
    public async Task A_mail_model_that_is_no_object_is_refused()
    {
        var publish = () => app.PublishAsync(_setup.AdminUserId, _setup.TenantId, Milestone("1 May", "2 May") with { MailModel = "text", Recipients = [_bob] });

        await publish.ShouldThrowAsync<ArgumentException>();
    }

    private static NotificationMessage Milestone(string oldValue, string newValue, string comment = "On time") =>
        new(TestNotificationModule.Milestone, "Departed", "The shipment left the port.")
        {
            Link = "/shipments/1",
            MailModel = new { shipment = new { Number = "S-1" }, milestone_name = "ETA", OldValue = oldValue, new_value = newValue, comment },
        };

    private async Task<(Guid Tenant, Guid User)> AddTenantWithUserAsync(string email) =>
        await app.InDbAsync(null, async db =>
        {
            var tenant = new Tenant { Name = "Other", Identifier = "other" };
            var user = new User { UserName = email, NormalizedUserName = email.ToUpperInvariant(), Email = email, NormalizedEmail = email.ToUpperInvariant(), TenantId = tenant.Id };
            db.AddRange(tenant, user);
            await db.SaveChangesAsync(Ct);
            return (tenant.Id, user.Id);
        });

    private Task<List<OutgoingMail>> MailsToAsync(string to) =>
        app.InDbAsync(null, db => db.Set<OutgoingMail>().IgnoreQueryFilters().Where(m => m.To == to).ToListAsync(Ct));
}
