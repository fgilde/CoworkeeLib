using System.Security.Claims;
using Bunit;
using Coworkee.Client.Blazor.Components.Mail;
using Coworkee.Client.Blazor.Components.Users;
using Coworkee.Client.Blazor.Data;
using Coworkee.Client.Blazor.Data.Admin;
using Coworkee.Client.Blazor.Pages.Admin;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Mailing;
using Coworkee.Contracts.Notifications;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using NSubstitute;

namespace Coworkee.Client.Blazor.Tests;

/// <summary>The user selection (filter + added - left out, counted by the server), the Send notification page and the send for real of mail templates.</summary>
public sealed class UserSelectionTests : ClientTestBase
{
    private static readonly Guid AdaId = Guid.CreateVersion7();
    private static readonly Guid BobId = Guid.CreateVersion7();
    private readonly FakeODataClient _odata = new();
    private readonly IRenderedComponent<MudDialogProvider> _dialogs;

    public UserSelectionTests()
    {
        CoworkeeUserSelection.CountDelay = TimeSpan.Zero;
        JSInterop.Setup<MudBlazor.Extensions.Core.CssVariable[]>("MudExCssHelper.getCssVariables", _ => true).SetResult([]);
        Services.AddSingleton<IODataClient>(_odata);
        _odata.With("Users",
            new UserRow(AdaId, "ada@acme.test", "Ada", "Lovelace", true, null, DateTimeOffset.UtcNow),
            new UserRow(BobId, "bob@acme.test", "Bob", "Builder", false, null, DateTimeOffset.UtcNow));
        Api.CountUserSelectionAsync(default!, default).ReturnsForAnyArgs(call => call.Arg<UserSelection>() switch
        {
            { AllMatching: true, Excluded.Count: > 0 } => new UserSelectionCountDto(1, 1),
            { AllMatching: true } => new UserSelectionCountDto(2, 1),
            var s => new UserSelectionCountDto(s.Included?.Count ?? 0, s.Included?.Count ?? 0),
        });
        Api.GetUserNamesAsync(default!, default).ReturnsForAnyArgs(call => call.Arg<IReadOnlyList<Guid>>().ToDictionary(id => id, id => id == AdaId ? "Ada Lovelace" : "Bob Builder"));
        AddAuthorization().SetAuthorized("Admin").SetClaims(new Claim("sub", Guid.CreateVersion7().ToString()))
            .SetPolicies(Security.PermissionPolicy.For(NotificationPermissions.Send), Security.PermissionPolicy.For(IdentityPermissions.Users.View));
        Render<MudPopoverProvider>();
        Render<MudSnackbarProvider>();
        _dialogs = Render<MudDialogProvider>();
    }

    [Fact]
    public async Task The_selection_takes_the_tables_filter_leaves_out_and_adds_single_users_and_shows_the_servers_count()
    {
        UserSelection? value = null;
        var picker = Render<CoworkeeUserSelection>(p => p.Add(s => s.Filter, "IsActive eq true").Add(s => s.ValueChanged, v => value = v));

        picker.WaitForAssertion(() => picker.Find("[data-testid='selection-count']").TextContent.ShouldBe("1 recipients"));
        value.ShouldNotBeNull().Filter.ShouldBe("IsActive eq true");
        picker.Find("[data-testid='selection-inactive']").TextContent.ShouldContain("1");

        picker.Find("input[data-testid='choose-bob@acme.test'], [data-testid='choose-bob@acme.test'] input").Change(false);
        picker.WaitForAssertion(() => value!.Excluded.ShouldBe([BobId]));
        picker.Find("[data-testid='selection-excluded']").TextContent.ShouldContain("1 left out");

        picker.Find("input[data-testid='selection-all'], [data-testid='selection-all'] input").Change(false);
        picker.WaitForAssertion(() => (value!.AllMatching, value.Excluded).ShouldBe((false, null)));
        picker.WaitForAssertion(() => picker.Find("[data-testid='selection-count']").TextContent.ShouldBe("0 recipients"));

        picker.Find("input[data-testid='choose-ada@acme.test'], [data-testid='choose-ada@acme.test'] input").Change(true);
        picker.WaitForAssertion(() => value!.Included.ShouldBe([AdaId]));
        picker.WaitForAssertion(() => picker.Find("[data-testid='selection-added']").TextContent.ShouldContain("Ada Lovelace"));
        await Api.Received().CountUserSelectionAsync(Arg.Is<UserSelection>(s => !s.AllMatching && s.Included!.Single() == AdaId), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void A_change_of_users_counts_again()
    {
        var picker = Render<CoworkeeUserSelection>();
        picker.WaitForAssertion(() => Api.ReceivedWithAnyArgs(1).CountUserSelectionAsync(default!, default));

        Services.GetRequiredService<FakeRealtimeConnection>().Push("type:User");

        picker.WaitForAssertion(() => Api.ReceivedWithAnyArgs(2).CountUserSelectionAsync(default!, default));
    }

    [Fact]
    public async Task The_send_notification_page_sends_what_was_entered_to_the_selection_after_asking()
    {
        Api.GetNotificationPreferencesAsync(default).ReturnsForAnyArgs([
            new NotificationPreferenceDto("orders.shipped", null, "Order shipped", NotificationScope.All, true, false, NotificationScope.All, true, false)]);
        var page = Render<SendNotification>();
        page.WaitForAssertion(() => page.Find("[data-testid='selection-count']").TextContent.ShouldBe("1 recipients"));

        page.Find("input[data-testid='notification-title'], [data-testid='notification-title'] input").Change("Maintenance at {0}");
        page.Find("textarea[data-testid='notification-body'], [data-testid='notification-body'] textarea").Change("Save your work.");
        page.Find("textarea[data-testid='notification-arguments'], [data-testid='notification-arguments'] textarea").Change("18:00");
        page.Find("input[data-testid='notification-link'], [data-testid='notification-link'] input").Change("/status");
        page.WaitForAssertion(() => page.Find("[data-testid='notification-send']").TextContent.ShouldContain("Send to 1 users"));
        // the click waits for the confirmation: not awaited, the dialog is answered below
        var sending = page.Find("[data-testid='notification-send']").ClickAsync(new());
        _dialogs.WaitForAssertion(() => _dialogs.FindAll("button").Any(b => b.TextContent.Trim() == "Send").ShouldBeTrue());
        await _dialogs.FindAll("button").First(b => b.TextContent.Trim() == "Send").ClickAsync(new());
        await sending;

        await Api.Received(1).SendNotificationAsync(Arg.Is<SendNotificationRequest>(r =>
            r.Title == "Maintenance at {0}" && r.Body == "Save your work." && r.Arguments!.Single() == "18:00" && r.Link == "/status"
            && r.Stored && r.Toast && r.Mail == NotificationMail.Never && r.Type == "notification" && r.Recipients.AllMatching && r.Recipients.Filter == null), Arg.Any<CancellationToken>());
        page.WaitForAssertion(() => page.Find("input[data-testid='notification-title'], [data-testid='notification-title'] input").GetAttribute("value").ShouldBeNullOrEmpty());
    }

    [Fact]
    public void The_send_button_waits_for_a_title_and_recipients()
    {
        var page = Render<SendNotification>();
        page.WaitForAssertion(() => page.Find("[data-testid='selection-count']").TextContent.ShouldBe("1 recipients"));

        page.Find("[data-testid='notification-send']").HasAttribute("disabled").ShouldBeTrue();
        page.Find("input[data-testid='notification-title'], [data-testid='notification-title'] input").Change("Hello");
        page.WaitForAssertion(() => page.Find("[data-testid='notification-send']").HasAttribute("disabled").ShouldBeFalse());
    }

    [Fact]
    public async Task A_mail_template_is_previewed_for_the_first_recipient_and_sent_with_the_edited_values()
    {
        const string sample = """{"user":{"first_name":"Ada"},"login_url":"https://example.com"}""";
        Api.PreviewMailTemplateSendAsync(default!, default!, default).ReturnsForAnyArgs(new MailTemplateSendPreviewDto(1, 1, new RenderedMailDto("Welcome, Ada", "<p>Hello Ada</p>")));
        Api.SendMailTemplateAsync(default!, default!, default).ReturnsForAnyArgs(1);
        var template = new MailTemplateDto("Identity.Welcome", "en", "Welcome", "<p></p>", "Welcome", "<p></p>", false, [], false) { SampleModel = sample };
        var shown = Render<MudExShowHost>(p => p.Add(h => h.Show, () => MailTemplateSendDialog.ShowAsync(Services.GetRequiredService<IDialogService>(), Services.GetRequiredService<Localization.CoworkeeLocalizer>(), template)));
        _dialogs.WaitForAssertion(() => _dialogs.Find("[data-testid='mail-send-model']"));
        _dialogs.WaitForAssertion(() => _dialogs.Find("[data-testid='selection-count']").TextContent.ShouldBe("1 recipients"));
        _dialogs.Find("[data-testid='mail-send-send']").HasAttribute("disabled").ShouldBeTrue();

        await _dialogs.Find("[data-testid='mail-send-preview']").ClickAsync(new());

        _dialogs.WaitForAssertion(() => _dialogs.Find("[data-testid='mail-send-subject']").TextContent.ShouldBe("Welcome, Ada"));
        _dialogs.Find("[data-testid='mail-send-count']").TextContent.ShouldBe("1 recipients");
        _dialogs.Find("[data-testid='mail-send-frame']").GetAttribute("srcdoc").ShouldBe("<p>Hello Ada</p>");
        await _dialogs.Find("[data-testid='mail-send-send']").ClickAsync(new());
        await Api.Received(1).SendMailTemplateAsync("Identity.Welcome", Arg.Is<SendMailTemplateRequest>(r => r.Recipients.AllMatching && r.Model!.Contains("login_url")), Arg.Any<CancellationToken>());
        _dialogs.WaitForAssertion(() => _dialogs.FindAll("[data-testid='mail-send']").ShouldBeEmpty());
        _ = shown;
    }

    [Fact]
    public void The_template_page_offers_the_send_for_real_only_with_the_campaign_right_and_the_right_to_view_users()
    {
        Api.GetMailTemplatesAsync(default).ReturnsForAnyArgs([new MailTemplateSummaryDto("Identity.Welcome", "Welcome", ["en"], [], false, null)]);
        Api.GetMailTemplateAsync(default!, default!).ReturnsForAnyArgs(new MailTemplateDto("Identity.Welcome", "en", "Hi", "<p>hi</p>", "Hi", "<p>hi</p>", false, [], false));
        Localization.GetLanguagesAsync(default, default).ReturnsForAnyArgs([new Contracts.Localization.LanguageDto(null, "en", "English", true, true)]);
        Api.GetMyPermissionsAsync(default).ReturnsForAnyArgs([MailPermissions.Templates.Manage, MailPermissions.Campaigns.Send]);

        var page = Render<MailTemplateDetail>(p => p.Add(d => d.Name, "Identity.Welcome").Add(d => d.Culture, "en"));
        page.WaitForAssertion(() => page.Find("[data-testid='language-chip-en']"));
        page.FindAll("[data-testid='send-for-real']").ShouldBeEmpty();

        Api.GetMyPermissionsAsync(default).ReturnsForAnyArgs([MailPermissions.Templates.Manage, MailPermissions.Campaigns.Send, IdentityPermissions.Users.View]);
        page.InvokeAsync(() => Services.GetRequiredService<Security.PermissionStore>().Reset());

        page.WaitForAssertion(() => page.Find("[data-testid='send-for-real']"));
    }

    /// <summary>Runs a static ShowAsync from inside the renderer, as a page's click handler would.</summary>
    private sealed class MudExShowHost : Microsoft.AspNetCore.Components.ComponentBase
    {
        [Microsoft.AspNetCore.Components.Parameter] public Func<Task> Show { get; set; } = null!;

        protected override void OnInitialized() => _ = Show();
    }
}
