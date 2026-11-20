using System.Security.Claims;
using Bunit;
using Coworkee.Client.Blazor.Components;
using Coworkee.Contracts.Notifications;
using Coworkee.Contracts.Realtime;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Client.Blazor.Tests;

public sealed class ToastNotifierTests : ClientTestBase
{
    private readonly Guid _me = Guid.CreateVersion7();

    public ToastNotifierTests() => AddAuthorization().SetAuthorized("Ada").SetClaims(new Claim("sub", _me.ToString()), new Claim("sid", "mine"));

    private FakeRealtimeConnection Realtime => Services.GetRequiredService<FakeRealtimeConnection>();

    [Fact]
    public void It_follows_the_topics_a_target_can_reach()
    {
        Render<ToastNotifier>();

        Realtime.Invocations.Where(i => i.Method == RealtimeHubMethods.Subscribe).Select(i => i.Topic).ShouldBe(
            [RealtimeTopics.User(_me), RealtimeTopics.TenantEvents, RealtimeTopics.GlobalEvents, RealtimeTopics.Session("mine")], ignoreOrder: true);
    }

    [Fact]
    public async Task A_toast_shows_as_snackbar_with_its_arguments_filled_and_its_link_as_action()
    {
        var snackbars = Render<MudBlazor.MudSnackbarProvider>();
        Render<ToastNotifier>();

        Realtime.Push(RealtimeTopics.TenantEvents, ToastPayload.Event,
            new ToastPayload("Maintenance at {0}", "Save your work before {0}.", NotificationSeverity.Warning, "/status", ["18:00"]));

        snackbars.WaitForAssertion(() => snackbars.Markup.ShouldContain("Maintenance at 18:00"));
        snackbars.Markup.ShouldContain("Save your work before 18:00.");
        snackbars.Markup.ShouldContain("mud-alert-filled-warning");
        await snackbars.Find(".mud-snackbar-content-action button").ClickAsync(new());
        Services.GetRequiredService<NavigationManager>().Uri.ShouldEndWith("/status");
    }

    [Fact]
    public void A_text_without_arguments_shows_as_it_is_and_other_events_show_nothing()
    {
        var snackbars = Render<MudBlazor.MudSnackbarProvider>();
        Render<ToastNotifier>();

        Realtime.Push(RealtimeTopics.User(_me), RealtimeEventTypes.EntityChanged, new { entityType = "Notification" });
        Realtime.Push(RealtimeTopics.User(_me), ToastPayload.Event, new ToastPayload("50% {0} done"));

        snackbars.WaitForAssertion(() => snackbars.Markup.ShouldContain("50% {0} done"));
        snackbars.FindAll(".mud-snackbar").Count.ShouldBe(1);
    }
}
