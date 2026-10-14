using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Coworkee.Contracts;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Mailing;
using Coworkee.Contracts.Settings;
using Coworkee.Identity.Domain;
using Coworkee.Settings;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Mailing.Tests;

public sealed partial class MailCampaignTests(MailApp app) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync()
    {
        _setup = await app.SetupAsync();
        (await Admin.PostAsJsonAsync("/api/v1/mail/templates", new CreateMailTemplateRequest("News.1", "News", null), Ct)).EnsureSuccessStatusCode();
        (await Admin.PutAsJsonAsync("/api/v1/mail/templates/News.1/en", new SaveMailTemplateRequest("News for {{ user.display_name }}", "<p>Hello {{ user.display_name }}</p>"), Ct)).EnsureSuccessStatusCode();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Admin => app.As(_setup.AdminUserId, _setup.TenantId);

    [GeneratedRegex("href=\"([^\"]*/api/v1/mail/unsubscribe\\?token=[^\"]*)\"")]
    private static partial Regex UnsubscribeLink();

    [GeneratedRegex("name=\"token\" value=\"([^\"]*)\"")]
    private static partial Regex ConfirmToken();

    private static RecipientSourceDto Manual(params Guid[] ids) => new(RecipientSourceKind.Manual, UserIds: ids);

    private async Task<Guid> SeedUserAsync(string? email = null)
    {
        email ??= $"{Guid.NewGuid():N}@seed.test";
        return await app.InDbAsync(async db =>
        {
            var user = new User { TenantId = _setup.TenantId, UserName = email, NormalizedUserName = email.ToUpperInvariant(), Email = email, NormalizedEmail = email.ToUpperInvariant(), EmailConfirmed = true };
            db.Add(user);
            await db.SaveChangesAsync(Ct);
            return user.Id;
        });
    }

    private Task OptOutAsync(Guid userId) => app.InDbAsync(async db =>
    {
        db.Add(new SettingValue { Name = MailSettings.Newsletters, Scope = SettingScope.User, ScopeKey = userId, Value = "false" });
        await db.SaveChangesAsync(Ct);
        return 0;
    });

    private async Task<MailCampaignDto> CreateAsync(RecipientSourceDto source, string template = "News.1", HttpClient? client = null)
    {
        var response = await (client ?? Admin).PostAsJsonAsync("/api/v1/mail/campaigns", new SaveMailCampaignRequest("Spring", template, source), Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<MailCampaignDto>(Ct))!;
    }

    private Task<int> SetStatusAsync(Guid id, MailCampaignStatus status) =>
        app.InDbAsync(db => db.Set<MailCampaign>().Where(c => c.Id == id).ExecuteUpdateAsync(u => u.SetProperty(c => c.Status, status), Ct));

    private async Task<MailCampaign> RunAsync(Guid id, int pageSize = 500)
    {
        await app.AsActorAsync(_setup.AdminUserId, _setup.TenantId, async sp =>
        {
            var job = sp.GetRequiredService<MailCampaignJob>();
            job.PageSize = pageSize;
            await job.ExecuteAsync(id, Ct);
            return 0;
        });
        return await CampaignAsync(id);
    }

    private Task<MailCampaign> CampaignAsync(Guid id) => app.InDbAsync(db => db.Set<MailCampaign>().AsNoTracking().SingleAsync(c => c.Id == id, Ct));

    private Task<List<OutgoingMail>> MailsAsync(Guid campaignId) =>
        app.InDbAsync(db => db.Set<OutgoingMail>().AsNoTracking().Where(m => m.CampaignId == campaignId).ToListAsync(Ct));

    // the events are dropped so no delivery job touches the row
    private Task SeedMailAsync(Guid campaignId, OutgoingMailStatus status) => app.InDbAsync(async db =>
    {
        var mail = OutgoingMail.Queue($"{Guid.NewGuid():N}@seed.test", new RenderedMail("Subject", "<p>Body</p>"), "News.1", _setup.TenantId, DateTimeOffset.UtcNow, campaignId);
        mail.ClearDomainEvents();
        mail.Status = status;
        db.Add(mail);
        await db.SaveChangesAsync(Ct);
        return 0;
    });

    [Fact]
    public async Task Sending_queues_one_mail_per_eligible_recipient_with_the_campaign_id()
    {
        var eligible = new[] { await SeedUserAsync(), await SeedUserAsync(), await SeedUserAsync() };
        var optedOut = await SeedUserAsync();
        await OptOutAsync(optedOut);
        var other = await app.CreateTenantAdminAsync();
        var campaign = await CreateAsync(Manual([.. eligible, optedOut, other.UserId]));
        await SetStatusAsync(campaign.Id, MailCampaignStatus.Scheduled);

        var finished = await RunAsync(campaign.Id);

        var mails = await MailsAsync(campaign.Id);
        var expected = await app.InDbAsync(db => db.Set<User>().Where(u => eligible.Contains(u.Id)).Select(u => u.Email!).ToListAsync(Ct));
        mails.Select(m => m.To).ShouldBe(expected, ignoreOrder: true);
        finished.ShouldSatisfyAllConditions(
            c => c.Status.ShouldBe(MailCampaignStatus.Sent), c => c.Total.ShouldBe(3), c => c.StartedAt.ShouldNotBeNull(), c => c.FinishedAt.ShouldNotBeNull(), c => c.Error.ShouldBeNull());
        mails.ShouldAllBe(m => m.TemplateName == "News.1" && m.Subject.StartsWith("News for "));
    }

    [Fact]
    public async Task Mails_contain_the_unsubscribe_link_and_it_opts_out()
    {
        var user = await SeedUserAsync();
        var campaign = await CreateAsync(Manual(user));
        await SetStatusAsync(campaign.Id, MailCampaignStatus.Scheduled);
        await RunAsync(campaign.Id);
        var html = (await MailsAsync(campaign.Id)).Single().HtmlBody;
        var client = app.App.GetTestClient();
        var link = new Uri(WebUtility.HtmlDecode(UnsubscribeLink().Match(html).Groups[1].Value)).PathAndQuery;
        Task<bool> OptedOutAsync() => app.InDbAsync(db => db.Set<SettingValue>().AnyAsync(v => v.Name == MailSettings.Newsletters && v.ScopeKey == user, Ct));

        var page = await client.GetAsync(link, Ct);

        page.StatusCode.ShouldBe(HttpStatusCode.OK);
        page.Content.Headers.ContentType!.MediaType.ShouldBe("text/html");
        page.Headers.CacheControl!.NoStore.ShouldBeTrue();
        page.Headers.GetValues("Referrer-Policy").ShouldBe(["no-referrer"]);
        var confirm = await page.Content.ReadAsStringAsync(Ct);
        confirm.ShouldContain("Do you want to stop receiving newsletters?");
        (await OptedOutAsync()).ShouldBeFalse("opening the link must not change anything");
        var token = WebUtility.HtmlDecode(ConfirmToken().Match(confirm).Groups[1].Value);

        var done = await client.PostAsync("/api/v1/mail/unsubscribe", new FormUrlEncodedContent([new("token", token)]), Ct);

        done.StatusCode.ShouldBe(HttpStatusCode.OK);
        done.Headers.CacheControl!.NoStore.ShouldBeTrue();
        (await done.Content.ReadAsStringAsync(Ct)).ShouldContain("You will no longer receive newsletters.");
        (await app.InDbAsync(db => db.Set<SettingValue>().Where(v => v.Name == MailSettings.Newsletters && v.ScopeKey == user).Select(v => v.Value).SingleAsync(Ct))).ShouldBe("false");
        var second = await CreateAsync(Manual(user));
        await SetStatusAsync(second.Id, MailCampaignStatus.Scheduled);
        (await RunAsync(second.Id)).Total.ShouldBe(0);
        (await MailsAsync(second.Id)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Mail_links_are_absolute_from_the_public_app_url()
    {
        var campaign = await CreateAsync(Manual(await SeedUserAsync()));
        await SetStatusAsync(campaign.Id, MailCampaignStatus.Scheduled);
        await RunAsync(campaign.Id);

        var link = WebUtility.HtmlDecode(UnsubscribeLink().Match((await MailsAsync(campaign.Id)).Single().HtmlBody).Groups[1].Value);

        link.ShouldStartWith("https://app.test/api/v1/mail/unsubscribe?token=");
    }

    [Fact]
    public async Task Scheduling_is_refused_and_the_job_fails_without_the_public_app_url()
    {
        var options = app.App.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<Coworkee.Contracts.Configuration.NotificationOptions>>().Value;
        var campaign = await CreateAsync(Manual(await SeedUserAsync()));
        var queued = await CreateAsync(Manual(await SeedUserAsync()));
        await SetStatusAsync(queued.Id, MailCampaignStatus.Scheduled);
        options.PublicAppUrl = null;
        try
        {
            var response = await Admin.PostAsJsonAsync($"/api/v1/mail/campaigns/{campaign.Id}/schedule", new ScheduleMailCampaignRequest(null), Ct);
            var failed = await RunAsync(queued.Id);

            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await response.Content.ReadAsStringAsync(Ct)).ShouldContain("Coworkee:Notifications:PublicAppUrl");
            (await CampaignAsync(campaign.Id)).Status.ShouldBe(MailCampaignStatus.Draft);
            failed.ShouldSatisfyAllConditions(c => c.Status.ShouldBe(MailCampaignStatus.Failed), c => c.Error!.ShouldContain("PublicAppUrl"));
            (await MailsAsync(queued.Id)).ShouldBeEmpty();
        }
        finally
        {
            options.PublicAppUrl = "https://app.test";
        }
    }

    [Fact]
    public async Task All_pages_are_queued()
    {
        var users = new[] { await SeedUserAsync(), await SeedUserAsync(), await SeedUserAsync() };
        var campaign = await CreateAsync(Manual(users));
        await SetStatusAsync(campaign.Id, MailCampaignStatus.Scheduled);

        var finished = await RunAsync(campaign.Id, pageSize: 1);

        finished.ShouldSatisfyAllConditions(c => c.Status.ShouldBe(MailCampaignStatus.Sent), c => c.Total.ShouldBe(3));
        (await MailsAsync(campaign.Id)).Count.ShouldBe(3);
    }

    [Fact]
    public async Task Template_and_layout_are_resolved_once_per_campaign_and_culture()
    {
        var campaign = await CreateAsync(Manual(await SeedUserAsync(), await SeedUserAsync(), await SeedUserAsync()));
        await SetStatusAsync(campaign.Id, MailCampaignStatus.Scheduled);
        var renderer = default(CountingRenderer);

        await app.AsActorAsync(_setup.AdminUserId, _setup.TenantId, async sp =>
        {
            renderer = new CountingRenderer(sp.GetRequiredService<IMailTemplateRenderer>());
            await ActivatorUtilities.CreateInstance<MailCampaignJob>(sp, renderer).ExecuteAsync(campaign.Id, Ct);
            return 0;
        });

        renderer!.Resolved.ShouldBe(2);
        (await MailsAsync(campaign.Id)).Count.ShouldBe(3);
    }

    [Fact]
    public async Task A_transient_failure_saving_the_final_status_is_retried()
    {
        var campaign = await CreateAsync(Manual(await SeedUserAsync()));
        await SetStatusAsync(campaign.Id, MailCampaignStatus.Scheduled);

        // clock calls: claim, one mail, final status
        await RunWithClockAsync(campaign.Id, new FlakyClock(failOn: [3]));

        (await CampaignAsync(campaign.Id)).ShouldSatisfyAllConditions(c => c.Status.ShouldBe(MailCampaignStatus.Sent), c => c.Error.ShouldBeNull());
    }

    [Fact]
    public async Task A_persistent_failure_saving_the_final_status_marks_the_campaign_failed()
    {
        var campaign = await CreateAsync(Manual(await SeedUserAsync()));
        await SetStatusAsync(campaign.Id, MailCampaignStatus.Scheduled);

        await RunWithClockAsync(campaign.Id, new FlakyClock(failOn: [3, 4]));

        (await CampaignAsync(campaign.Id)).ShouldSatisfyAllConditions(
            c => c.Status.ShouldBe(MailCampaignStatus.Failed), c => c.Error!.ShouldContain("final status"), c => c.FinishedAt.ShouldNotBeNull());
    }

    [Fact]
    public async Task The_test_mail_renders_without_a_footer_when_no_absolute_address_exists()
    {
        var campaign = await CreateAsync(Manual(await SeedUserAsync()));
        var options = app.App.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<Coworkee.Contracts.Configuration.NotificationOptions>>().Value;
        options.PublicAppUrl = null;
        try
        {
            var result = await app.AsActorAsync(_setup.AdminUserId, _setup.TenantId, async sp =>
            {
                var handled = await ActivatorUtilities.CreateInstance<MailCampaignTestHandler>(sp).HandleAsync(new SendMailCampaignTest(campaign.Id), Ct);
                await sp.GetRequiredService<MailTestDbContext>().SaveChangesAsync(Ct);
                return handled;
            });

            result.IsSuccess.ShouldBeTrue();
            var mail = await app.InDbAsync(db => db.Set<OutgoingMail>().AsNoTracking().SingleAsync(m => m.To == "admin@acme.test" && m.TemplateName == "News.1" && m.CampaignId == null, Ct));
            mail.HtmlBody.ShouldNotContain("unsubscribe");
        }
        finally
        {
            options.PublicAppUrl = "https://app.test";
        }
    }

    private Task RunWithClockAsync(Guid id, TimeProvider clock) => app.AsActorAsync(_setup.AdminUserId, _setup.TenantId, async sp =>
    {
        await ActivatorUtilities.CreateInstance<MailCampaignJob>(sp, clock).ExecuteAsync(id, Ct);
        return 0;
    });

    private sealed class FlakyClock(int[] failOn) : TimeProvider
    {
        private int _calls;

        public override DateTimeOffset GetUtcNow() =>
            failOn.Contains(Interlocked.Increment(ref _calls)) ? throw new InvalidOperationException("transient failure") : DateTimeOffset.UtcNow;
    }

    private sealed class CountingRenderer(IMailTemplateRenderer inner) : IMailTemplateRenderer
    {
        public int Resolved { get; private set; }

        public Task<RenderedMail> RenderAsync(string name, object model, string? culture, CancellationToken cancellationToken) => inner.RenderAsync(name, model, culture, cancellationToken);

        public Task<RenderedMail> RenderContentAsync(MailTemplateContent content, object model, string? culture, CancellationToken cancellationToken) =>
            inner.RenderContentAsync(content, model, culture, cancellationToken);

        public Task<RenderedMail> RenderContentAsync(MailTemplateContent content, MailTemplateContent layout, object model, CancellationToken cancellationToken) =>
            inner.RenderContentAsync(content, layout, model, cancellationToken);

        public Task<MailTemplateContent> ResolveAsync(string name, string? culture, CancellationToken cancellationToken)
        {
            Resolved++;
            return inner.ResolveAsync(name, culture, cancellationToken);
        }
    }

    [Fact]
    public async Task Unreadable_source_marks_the_campaign_failed()
    {
        var id = await app.InDbAsync(async db =>
        {
            var created = new MailCampaign { TenantId = _setup.TenantId, Name = "Broken", TemplateName = "News.1", RecipientSource = "not json", Status = MailCampaignStatus.Scheduled, CreatedBy = _setup.AdminUserId, CreatedAt = DateTimeOffset.UtcNow };
            db.Add(created);
            await db.SaveChangesAsync(Ct);
            return created.Id;
        });

        var finished = await RunAsync(id);

        finished.ShouldSatisfyAllConditions(c => c.Status.ShouldBe(MailCampaignStatus.Failed), c => c.Error.ShouldNotBeNullOrWhiteSpace());
    }

    [Fact]
    public async Task Tampered_token_is_rejected()
    {
        var user = await SeedUserAsync();
        var token = app.App.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("Another.Purpose").Protect($"{user:N}:{_setup.TenantId:N}");
        var valid = await app.AsActorAsync(null, null, sp => Task.FromResult(sp.GetRequiredService<IMailUnsubscribeLinks>().Create(user, _setup.TenantId)));
        var good = Uri.UnescapeDataString(valid[(valid.IndexOf("token=", StringComparison.Ordinal) + 6)..]);
        var broken = good[..^3] + (good[^3] == 'A' ? "B" : "A") + good[^2..];
        var client = app.App.GetTestClient();

        foreach (var bad in new[] { token, broken, string.Empty })
        {
            (await client.GetAsync($"/api/v1/mail/unsubscribe?token={Uri.EscapeDataString(bad)}", Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest, "GET " + bad);
            (await client.PostAsync("/api/v1/mail/unsubscribe", new FormUrlEncodedContent([new("token", bad)]), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest, "POST " + bad);
        }

        (await client.GetAsync("/api/v1/mail/unsubscribe", Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await client.PostAsync("/api/v1/mail/unsubscribe", JsonContent.Create(new { token = good }), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await app.InDbAsync(db => db.Set<SettingValue>().AnyAsync(v => v.Name == MailSettings.Newsletters && v.ScopeKey == user, Ct))).ShouldBeFalse();
    }

    [Fact]
    public async Task A_malformed_form_is_a_bad_request()
    {
        var content = new StringContent("--x\r\nbroken", System.Text.Encoding.UTF8, "multipart/form-data");

        (await app.App.GetTestClient().PostAsync("/api/v1/mail/unsubscribe", content, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task The_confirm_form_posts_below_the_path_base()
    {
        var user = await SeedUserAsync();
        var link = await app.AsActorAsync(null, null, sp => Task.FromResult(sp.GetRequiredService<IMailUnsubscribeLinks>().Create(user, _setup.TenantId)));

        var html = await app.AsActorAsync(null, null, async sp =>
        {
            var http = new Microsoft.AspNetCore.Http.DefaultHttpContext { RequestServices = sp };
            http.Request.PathBase = "/app";
            http.Response.Body = new MemoryStream();
            var result = await sp.GetRequiredService<MailUnsubscribe>().ConfirmAsync(http, Uri.UnescapeDataString(link[(link.IndexOf("token=", StringComparison.Ordinal) + 6)..]), Ct);
            await result.ExecuteAsync(http);
            return System.Text.Encoding.UTF8.GetString(((MemoryStream)http.Response.Body).ToArray());
        });

        html.ShouldContain("action=\"/app/api/v1/mail/unsubscribe\"");
    }

    [Fact]
    public async Task Only_custom_templates_can_be_sent()
    {
        foreach (var template in new[] { "Identity.Welcome", "Test.Hello", "Nope.1" })
        {
            (await Admin.PostAsJsonAsync("/api/v1/mail/campaigns", new SaveMailCampaignRequest("X", template, Manual(_setup.AdminUserId)), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest, template);
        }
    }

    [Fact]
    public async Task Cancel_stops_the_running_job_and_removes_its_queued_mails()
    {
        var users = new[] { await SeedUserAsync(), await SeedUserAsync(), await SeedUserAsync() };
        var campaign = await CreateAsync(Manual(users));
        await SetStatusAsync(campaign.Id, MailCampaignStatus.Scheduled);
        app.AfterPage = async () => (await Admin.PostAsync($"/api/v1/mail/campaigns/{campaign.Id}/cancel", null, Ct)).EnsureSuccessStatusCode();

        var finished = await RunAsync(campaign.Id, pageSize: 1);

        var mails = await MailsAsync(campaign.Id);
        app.AfterPage.ShouldBeNull();
        finished.ShouldSatisfyAllConditions(c => c.Status.ShouldBe(MailCampaignStatus.Cancelled), c => c.Total.ShouldBe(1));
        mails.Count.ShouldBeLessThanOrEqualTo(1);
        mails.ShouldNotContain(m => m.Status == OutgoingMailStatus.Queued);
    }

    [Fact]
    public async Task Cancel_removes_queued_mails_and_keeps_sent_ones()
    {
        var campaign = await CreateAsync(Manual(_setup.AdminUserId));
        await SetStatusAsync(campaign.Id, MailCampaignStatus.Scheduled);
        await SeedMailAsync(campaign.Id, OutgoingMailStatus.Sent);
        await SeedMailAsync(campaign.Id, OutgoingMailStatus.Queued);
        await SeedMailAsync(campaign.Id, OutgoingMailStatus.Queued);

        (await Admin.PostAsync($"/api/v1/mail/campaigns/{campaign.Id}/cancel", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await MailsAsync(campaign.Id)).Select(m => m.Status).ShouldBe([OutgoingMailStatus.Sent]);
        (await CampaignAsync(campaign.Id)).Status.ShouldBe(MailCampaignStatus.Cancelled);
        (await Admin.PostAsync($"/api/v1/mail/campaigns/{campaign.Id}/cancel", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Invalid_filter_marks_the_campaign_failed()
    {
        foreach (var filter in new[] { "nonsense eq 1", "((" })
        {
            var campaign = await app.InDbAsync(async db =>
            {
                var created = new MailCampaign
                {
                    TenantId = _setup.TenantId,
                    Name = "Broken",
                    TemplateName = "News.1",
                    RecipientSource = RecipientSourceJson.Write(new RecipientSourceDto(RecipientSourceKind.Query, Filter: filter)),
                    Status = MailCampaignStatus.Scheduled,
                    CreatedBy = _setup.AdminUserId,
                    CreatedAt = DateTimeOffset.UtcNow,
                };
                db.Add(created);
                await db.SaveChangesAsync(Ct);
                return created.Id;
            });

            var finished = await RunAsync(campaign);

            finished.Status.ShouldBe(MailCampaignStatus.Failed, filter);
            finished.Error.ShouldNotBeNullOrWhiteSpace();
            finished.Error.ShouldNotBe("One or more validation errors occurred.");
            finished.FinishedAt.ShouldNotBeNull();
        }
    }

    [Fact]
    public async Task Template_in_use_cannot_be_deleted()
    {
        var campaign = await CreateAsync(Manual(_setup.AdminUserId));

        (await Admin.DeleteAsync("/api/v1/mail/templates/News.1", Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        (await Admin.DeleteAsync($"/api/v1/mail/campaigns/{campaign.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await Admin.DeleteAsync("/api/v1/mail/templates/News.1", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Counts_come_from_the_mail_log()
    {
        var campaign = await CreateAsync(Manual(_setup.AdminUserId));
        var untouched = await CreateAsync(Manual(_setup.AdminUserId));
        await SeedMailAsync(campaign.Id, OutgoingMailStatus.Sent);
        await SeedMailAsync(campaign.Id, OutgoingMailStatus.Failed);
        await SeedMailAsync(campaign.Id, OutgoingMailStatus.Queued);

        var single = (await Admin.GetFromJsonAsync<MailCampaignDto>($"/api/v1/mail/campaigns/{campaign.Id}", Ct))!;
        var list = (await Admin.GetFromJsonAsync<MailCampaignDto[]>("/api/v1/mail/campaigns", Ct))!;

        foreach (var dto in new[] { single, list.Single(c => c.Id == campaign.Id) })
        {
            (dto.Sent, dto.Failed, dto.Queued).ShouldBe((1, 1, 1));
        }

        list.Single(c => c.Id == untouched.Id).ShouldSatisfyAllConditions(c => c.Sent.ShouldBe(0), c => c.Failed.ShouldBe(0), c => c.Queued.ShouldBe(0));
    }

    [Fact]
    public async Task Outgoing_mails_filter_by_campaign()
    {
        var first = await CreateAsync(Manual(_setup.AdminUserId));
        var second = await CreateAsync(Manual(_setup.AdminUserId));
        await SeedMailAsync(first.Id, OutgoingMailStatus.Sent);
        await SeedMailAsync(first.Id, OutgoingMailStatus.Failed);
        await SeedMailAsync(second.Id, OutgoingMailStatus.Sent);

        var page = (await Admin.GetFromJsonAsync<PagedResult<OutgoingMailDto>>($"/api/v1/mail/outgoing?campaignId={first.Id}", Ct))!;

        page.Items.Count.ShouldBe(2);
        page.Items.ShouldAllBe(m => m.CampaignId == first.Id);
    }

    [Fact]
    public async Task Draft_can_be_changed_but_not_after_scheduling()
    {
        var campaign = await CreateAsync(Manual(_setup.AdminUserId));
        var change = new SaveMailCampaignRequest("Renamed", "News.1", Manual(_setup.AdminUserId));

        (await Admin.PutAsJsonAsync($"/api/v1/mail/campaigns/{campaign.Id}", change, Ct)).EnsureSuccessStatusCode();
        var future = DateTimeOffset.UtcNow.AddDays(1);
        var scheduled = await (await Admin.PostAsJsonAsync($"/api/v1/mail/campaigns/{campaign.Id}/schedule", new ScheduleMailCampaignRequest(future), Ct)).Content.ReadFromJsonAsync<MailCampaignDto>(Ct);

        scheduled!.ShouldSatisfyAllConditions(c => c.Name.ShouldBe("Renamed"), c => c.Status.ShouldBe(MailCampaignStatus.Scheduled), c => c.ScheduledFor!.Value.ShouldBe(future, TimeSpan.FromSeconds(1)));
        (await Admin.PutAsJsonAsync($"/api/v1/mail/campaigns/{campaign.Id}", change, Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await Admin.DeleteAsync($"/api/v1/mail/campaigns/{campaign.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await MailsAsync(campaign.Id)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_time_in_the_past_is_a_validation_error()
    {
        var campaign = await CreateAsync(Manual(await SeedUserAsync()));

        var response = await Admin.PostAsJsonAsync($"/api/v1/mail/campaigns/{campaign.Id}/schedule", new ScheduleMailCampaignRequest(DateTimeOffset.UtcNow.AddHours(-2)), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CampaignAsync(campaign.Id)).Status.ShouldBe(MailCampaignStatus.Draft);
    }

    [Fact]
    public async Task Scheduling_without_a_time_sends_through_the_queue()
    {
        var user = await SeedUserAsync();
        var campaign = await CreateAsync(Manual(user));

        (await Admin.PostAsJsonAsync($"/api/v1/mail/campaigns/{campaign.Id}/schedule", new ScheduleMailCampaignRequest(null), Ct)).EnsureSuccessStatusCode();

        var email = await app.InDbAsync(db => db.Set<User>().Where(u => u.Id == user).Select(u => u.Email!).SingleAsync(Ct));
        await MailApp.Eventually(async () => (await app.MessagesToAsync(email)).Any(m => m.Subject.StartsWith("News for ")));
        await MailApp.Eventually(async () => (await Admin.GetFromJsonAsync<MailCampaignDto>($"/api/v1/mail/campaigns/{campaign.Id}", Ct))!.Status == MailCampaignStatus.Sent);
        (await Admin.GetFromJsonAsync<MailCampaignDto>($"/api/v1/mail/campaigns/{campaign.Id}", Ct))!.Total.ShouldBe(1);
    }

    [Fact]
    public async Task Test_mail_goes_to_the_current_user_without_a_campaign_id()
    {
        var campaign = await CreateAsync(Manual(await SeedUserAsync()));

        (await Admin.PostAsync($"/api/v1/mail/campaigns/{campaign.Id}/test", null, Ct)).EnsureSuccessStatusCode();

        var mail = await app.InDbAsync(db => db.Set<OutgoingMail>().AsNoTracking().SingleAsync(m => m.To == "admin@acme.test" && m.TemplateName == "News.1", Ct));
        mail.CampaignId.ShouldBeNull();
        mail.HtmlBody.ShouldContain("/api/v1/mail/unsubscribe?token=");
    }

    [Fact]
    public async Task Other_tenants_do_not_see_the_campaign()
    {
        var campaign = await CreateAsync(Manual(_setup.AdminUserId));
        var other = await app.CreateTenantAdminAsync();
        var tenantB = app.As(other.UserId, other.TenantId);

        (await tenantB.GetAsync($"/api/v1/mail/campaigns/{campaign.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await tenantB.PostAsync($"/api/v1/mail/campaigns/{campaign.Id}/cancel", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await tenantB.DeleteAsync($"/api/v1/mail/campaigns/{campaign.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await tenantB.GetFromJsonAsync<MailCampaignDto[]>("/api/v1/mail/campaigns", Ct))!.ShouldBeEmpty();
    }
}
