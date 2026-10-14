using AngleSharp.Dom;
using Bunit;
using Coworkee.Client.Blazor.Data;
using Coworkee.Client.Blazor.Data.Admin;
using Coworkee.Client.Blazor.Pages.Admin;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Mailing;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using NSubstitute;

namespace Coworkee.Client.Blazor.Tests;

public sealed class MailCampaignsPageTests : ClientTestBase
{
    private static readonly Guid Sending = Guid.CreateVersion7();
    private static readonly Guid Planned = Guid.CreateVersion7();
    private static readonly Guid Draft = Guid.CreateVersion7();
    private static readonly UserRow Ada = new(Guid.CreateVersion7(), "ada@acme.test", "Ada", "Lovelace", true, null, DateTimeOffset.UtcNow);
    private static readonly UserRow Bob = new(Guid.CreateVersion7(), "bob@acme.test", "Bob", "Baker", true, null, DateTimeOffset.UtcNow);

    private readonly IRenderedComponent<MudPopoverProvider> _popovers;
    private readonly IRenderedComponent<MudDialogProvider> _dialogs;
    private readonly List<RecipientSourceDto> _previews = [];
    private readonly List<string?> _filters = [];
    private IReadOnlyList<MailCampaignDto> _campaigns;

    public MailCampaignsPageTests()
    {
        _campaigns =
        [
            Campaign(Sending, "Spring sale", MailCampaignStatus.Sending, total: 10, sent: 3, failed: 1),
            Campaign(Planned, "Autumn news", MailCampaignStatus.Scheduled, scheduledFor: DateTimeOffset.UtcNow.AddDays(1)),
            Campaign(Draft, "Old draft", MailCampaignStatus.Draft),
        ];
        var odata = new FakeODataClient().With("Users", Ada, Bob);
        odata.Queried += (_, query) => _filters.Add(query.Filter);
        Services.AddSingleton<IODataClient>(odata);
        AddAuthorization().SetAuthorized("Ada");
        Api.GetMailCampaignsAsync(default).ReturnsForAnyArgs(_ => _campaigns);
        Api.GetMailTemplatesAsync(default).ReturnsForAnyArgs([
            new MailTemplateSummaryDto("Identity.Welcome", "Welcome", ["en"], [], false, null),
            new MailTemplateSummaryDto("News.1", "News", ["en"], [], true, null),
            new MailTemplateSummaryDto("Offers.1", "Offers", ["en"], [], true, null)]);
        Api.GetRecipientListsAsync(default).ReturnsForAnyArgs([]);
        Api.GetRolesAsync(default).ReturnsForAnyArgs([new RoleDto(Guid.CreateVersion7(), "Editors", null, false)]);
        Api.PreviewRecipientsAsync(default!, default).ReturnsForAnyArgs(call =>
        {
            _previews.Add(call.Arg<RecipientSourceDto>());
            return new RecipientPreviewDto(42, 5, [new RecipientRowDto(Ada.Id, Ada.Email, "Ada Lovelace", null)]);
        });
        Api.SaveMailCampaignAsync(default, default!, default).ReturnsForAnyArgs(Campaign(Draft, "News", MailCampaignStatus.Draft));
        Api.ScheduleMailCampaignAsync(default, default, default).ReturnsForAnyArgs(Campaign(Draft, "News", MailCampaignStatus.Scheduled));
        _popovers = Render<MudPopoverProvider>();
        _dialogs = Render<MudDialogProvider>();
    }

    private static MailCampaignDto Campaign(Guid id, string name, MailCampaignStatus status, int total = 0, int sent = 0, int failed = 0, DateTimeOffset? scheduledFor = null) =>
        new(id, name, "News.1", new RecipientSourceDto(RecipientSourceKind.Preset, RecipientPreset.AllActive), status, scheduledFor, total, sent, failed, 0,
            DateTimeOffset.UtcNow, null, null, null);

    private async Task PickAsync(string testId, string text)
    {
        _dialogs.WaitForElement($"[data-testid='{testId}']").MouseDown();
        await _popovers.WaitForElements(".mud-list-item").First(i => i.TextContent.Contains(text, StringComparison.Ordinal)).ClickAsync(new());
    }

    private IRenderedComponent<MailCampaigns> OpenWizard()
    {
        var page = Render<MailCampaigns>();
        // the click returns only when the wizard closes
        _ = page.WaitForElement("[data-testid='new-campaign']").ClickAsync(new());
        _dialogs.WaitForElement("[data-testid='campaign-template']");
        return page;
    }

    private async Task ChooseTemplateAndContinueAsync()
    {
        await PickAsync("campaign-template", "News");
        _dialogs.WaitForAssertion(() => _dialogs.Find("input[data-testid='campaign-name'], [data-testid='campaign-name'] input").GetAttribute("value").ShouldBe("News"));
        await _dialogs.Find("[data-testid='wizard-next']").ClickAsync(new());
    }

    private async Task ToSendStepAsync()
    {
        await ChooseTemplateAndContinueAsync();
        _dialogs.WaitForAssertion(() => _dialogs.Find("[data-testid='recipient-count']"));
        await _dialogs.WaitForElement("[data-testid='wizard-next']:not([disabled])").ClickAsync(new());
    }

    private IElement Button(string text) => _dialogs.FindAll("button").Last(b => b.TextContent.Trim() == text);

    [Fact]
    public void Page_lists_campaigns_with_progress()
    {
        var page = Render<MailCampaigns>();

        page.WaitForElement($"[data-testid='campaign-progress-{Sending}']").TextContent.ShouldBe("3/10");
        page.Find($"[data-testid='campaign-failed-{Sending}']").TextContent.ShouldContain("1");
        page.Find($"[data-testid='campaign-{Sending}']").TextContent.ShouldContain("Spring sale");
        page.Find($"[data-testid='campaign-{Sending}'] .mud-chip").TextContent.ShouldBe("Sending");
        page.Find($"[data-testid='campaign-log-{Sending}']").GetAttribute("href")!.ShouldEndWith($"/admin/mail/log?campaign={Sending}");
        page.FindAll($"[data-testid='campaign-failed-{Planned}']").ShouldBeEmpty();
    }

    [Fact]
    public void Cancel_is_offered_for_scheduled_and_sending_campaigns()
    {
        var page = Render<MailCampaigns>();

        page.WaitForElement($"[data-testid='cancel-campaign-{Sending}']");
        page.FindAll($"[data-testid='cancel-campaign-{Planned}']").Count.ShouldBe(1);
        page.FindAll($"[data-testid='cancel-campaign-{Draft}']").ShouldBeEmpty();
        page.FindAll($"[data-testid='delete-campaign-{Draft}']").Count.ShouldBe(1);
        page.FindAll($"[data-testid='delete-campaign-{Sending}']").ShouldBeEmpty();
    }

    [Fact]
    public async Task Cancelling_asks_first_and_reloads()
    {
        var page = Render<MailCampaigns>();

        var cancelling = page.WaitForElement($"[data-testid='cancel-campaign-{Planned}']").ClickAsync(new());
        _dialogs.WaitForAssertion(() => Button("Cancel campaign").ShouldNotBeNull());
        await Button("Cancel campaign").ClickAsync(new());
        await cancelling;

        await Api.Received(1).CancelMailCampaignAsync(Planned, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void The_list_reloads_while_a_campaign_is_sending()
    {
        var page = Render<MailCampaigns>(p => p.Add(c => c.PollInterval, TimeSpan.FromMilliseconds(50)));
        page.WaitForElement($"[data-testid='campaign-progress-{Sending}']").TextContent.ShouldBe("3/10");

        _campaigns = [Campaign(Sending, "Spring sale", MailCampaignStatus.Sent, total: 10, sent: 10)];

        page.WaitForAssertion(() => page.Find($"[data-testid='campaign-progress-{Sending}']").TextContent.ShouldBe("10/10"));
    }

    [Fact]
    public async Task Wizard_offers_only_custom_templates()
    {
        OpenWizard();

        _dialogs.Find("[data-testid='campaign-template']").MouseDown();

        var items = _popovers.WaitForElements(".mud-list-item").Select(i => i.TextContent.Trim()).ToList();
        items.ShouldBe(["News", "Offers"]);
    }

    [Fact]
    public async Task Preset_with_days_previews_count()
    {
        OpenWizard();
        await ChooseTemplateAndContinueAsync();

        await PickAsync("smart-choice", "Not signed in");
        _dialogs.WaitForAssertion(() => _previews.ShouldContain(s => s.Preset == RecipientPreset.NotSignedInSince && s.Days == 30));
        _dialogs.WaitForAssertion(() => _dialogs.Find("[data-testid='recipient-count']").TextContent.ShouldContain("42"));
        _dialogs.Find("[data-testid='recipient-excluded']").TextContent.ShouldContain("5");

        await _dialogs.Find("[data-testid='preset-days'] input, input[data-testid='preset-days']").ChangeAsync(new ChangeEventArgs { Value = "14" });
        _dialogs.WaitForAssertion(() => _previews.ShouldContain(s => s.Preset == RecipientPreset.NotSignedInSince && s.Days == 14));
    }

    [Fact]
    public async Task Query_source_sends_the_odata_filter_and_can_be_saved_as_list()
    {
        Api.SaveRecipientListAsync(default!, default).ReturnsForAnyArgs(call => new RecipientListDto(Guid.CreateVersion7(), call.Arg<SaveRecipientListRequest>().Name, call.Arg<SaveRecipientListRequest>().Source));
        OpenWizard();
        await ChooseTemplateAndContinueAsync();

        await PickAsync("smart-choice", "Custom query");
        await _dialogs.WaitForElement("[data-testid='query-add-rule']").ClickAsync(new());
        await _dialogs.Find("[data-testid='query-value'] input, input[data-testid='query-value']").InputAsync(new ChangeEventArgs { Value = "a" });

        _dialogs.WaitForAssertion(() => _previews.ShouldContain(s => s.Kind == RecipientSourceKind.Query && !string.IsNullOrEmpty(s.Filter) && !string.IsNullOrEmpty(s.QueryJson)));

        var saving = _dialogs.Find("[data-testid='save-list']").ClickAsync(new());
        _dialogs.WaitForElement("form");
        _dialogs.FindAll("input[aria-describedby]")[0].Change("Gmail users");
        await _dialogs.Find("form").SubmitAsync();
        await saving;

        await Api.Received(1).SaveRecipientListAsync(Arg.Is<SaveRecipientListRequest>(r => r.Name == "Gmail users" && r.Source.Kind == RecipientSourceKind.Query && r.Source.Filter != null), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Manual_selection_sends_the_checked_ids()
    {
        OpenWizard();
        await ChooseTemplateAndContinueAsync();

        await _dialogs.WaitForElements(".mud-tab").First(t => t.TextContent.Contains("Manual selection", StringComparison.Ordinal)).ClickAsync(new());
        _dialogs.WaitForAssertion(() => _dialogs.FindAll(".mud-table-body input[type='checkbox']").Count.ShouldBe(2));
        foreach (var box in _dialogs.FindAll(".mud-table-body input[type='checkbox']"))
        {
            await box.ChangeAsync(new ChangeEventArgs { Value = true });
        }

        _dialogs.WaitForAssertion(() => _previews.ShouldContain(s =>
            s.Kind == RecipientSourceKind.Manual && s.UserIds != null && s.UserIds.Count == 2 && s.UserIds.Contains(Ada.Id) && s.UserIds.Contains(Bob.Id)));

        await _dialogs.Find("input[data-testid='table-search'], [data-testid='table-search'] input").InputAsync(new ChangeEventArgs { Value = "ada" });
        _dialogs.WaitForAssertion(() => _filters.ShouldContain(f => f != null && f.Contains("ada", StringComparison.Ordinal)));
        await _dialogs.Find("[data-testid='select-all-matching']").ClickAsync(new());

        _dialogs.WaitForAssertion(() => _previews.ShouldContain(s => s.Kind == RecipientSourceKind.Query && s.Filter != null && s.Filter.Contains("ada", StringComparison.Ordinal)));
        _dialogs.Find("[data-testid='all-matching']");
    }

    [Fact]
    public async Task Send_now_confirms_with_the_count()
    {
        OpenWizard();
        await ToSendStepAsync();

        var sending = _dialogs.Find("[data-testid='send-now']").ClickAsync(new());
        _dialogs.WaitForAssertion(() => _dialogs.Markup.ShouldContain("42 recipients now"));
        await Api.DidNotReceiveWithAnyArgs().ScheduleMailCampaignAsync(default, default, default);
        await Button("Confirm").ClickAsync(new());
        await sending;

        Received.InOrder(() =>
        {
            Api.SaveMailCampaignAsync(null, Arg.Is<SaveMailCampaignRequest>(r => r.Name == "News" && r.TemplateName == "News.1" && r.Recipients.Preset == RecipientPreset.AllActive), Arg.Any<CancellationToken>());
            Api.ScheduleMailCampaignAsync(Draft, null, Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task Schedule_sends_the_chosen_time_of_the_browser_zone_as_utc()
    {
        JSInterop.SetupModule("./_content/Coworkee.Client.Blazor/coworkee.js").Setup<int>("utcOffsetMinutes", _ => true).SetResult(-300);
        OpenWizard();
        await ToSendStepAsync();
        var date = new DateTime(2027, 1, 5);
        var time = new TimeSpan(9, 30, 0);

        _dialogs.Find("[data-testid='schedule']").HasAttribute("disabled").ShouldBeTrue();
        var datePicker = _dialogs.FindComponent<MudDatePicker>();
        var timePicker = _dialogs.FindComponent<MudTimePicker>();
        await _dialogs.InvokeAsync(() => datePicker.Instance.DateChanged.InvokeAsync(date));
        await _dialogs.InvokeAsync(() => timePicker.Instance.TimeChanged.InvokeAsync(time));
        var scheduling = _dialogs.Find("[data-testid='schedule']").ClickAsync(new());
        await Button("Confirm").ClickAsync(new());
        await scheduling;

        await Api.Received(1).ScheduleMailCampaignAsync(Draft, new DateTimeOffset(2027, 1, 5, 14, 30, 0, TimeSpan.Zero), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_test_mail_saves_the_draft_first()
    {
        OpenWizard();
        await ToSendStepAsync();

        await _dialogs.Find("[data-testid='send-test']").ClickAsync(new());

        await Api.Received(1).SendMailCampaignTestAsync(Draft, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void The_mail_log_filters_by_campaign()
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/admin/mail/log?campaign={Sending}");

        var page = Render<MailLog>();

        page.WaitForElement("[data-testid='campaign-filter']");
        _filters.ShouldContain($"CampaignId eq {Sending}");
    }

    [Fact]
    public void A_campaign_started_now_is_followed_from_scheduled_to_sending()
    {
        _campaigns = [Campaign(Planned, "Autumn news", MailCampaignStatus.Scheduled)];
        var page = Render<MailCampaigns>(p => p.Add(c => c.PollInterval, TimeSpan.FromMilliseconds(50)));
        page.WaitForAssertion(() => page.Find($"[data-testid='campaign-{Planned}'] .mud-chip").TextContent.ShouldBe("Scheduled"));

        _campaigns = [Campaign(Planned, "Autumn news", MailCampaignStatus.Sending, total: 4, sent: 1)];

        page.WaitForAssertion(() => page.Find($"[data-testid='campaign-{Planned}'] .mud-chip").TextContent.ShouldBe("Sending"));
    }

    [Fact]
    public void A_campaign_scheduled_far_ahead_is_not_polled()
    {
        _campaigns = [Campaign(Planned, "Autumn news", MailCampaignStatus.Scheduled, scheduledFor: DateTimeOffset.UtcNow.AddDays(1))];
        var page = Render<MailCampaigns>(p => p.Add(c => c.PollInterval, TimeSpan.FromMilliseconds(30)));
        page.WaitForElement($"[data-testid='campaign-{Planned}']");
        Api.ClearReceivedCalls();

        Thread.Sleep(300);

        Api.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Excluded_users_are_shown_also_when_there_are_none()
    {
        Api.PreviewRecipientsAsync(default!, default).ReturnsForAnyArgs(new RecipientPreviewDto(3, 0, []));
        OpenWizard();
        await ChooseTemplateAndContinueAsync();

        _dialogs.WaitForAssertion(() => _dialogs.Find("[data-testid='recipient-excluded']").TextContent.ShouldStartWith("0 "));
    }
}