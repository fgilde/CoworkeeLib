using Bunit;
using Coworkee.Client.Blazor.Components;
using Coworkee.Client.Blazor.Pages;
using Coworkee.Contracts;
using Coworkee.Contracts.Notifications;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Coworkee.Client.Blazor.Tests;

public sealed class NotificationTests : ClientTestBase
{
    private static readonly NotificationDto Unread = new(Guid.CreateVersion7(), "share", "A share for you", "Open it", "/shares/1", DateTimeOffset.UtcNow, null);
    private static readonly NotificationDto Read = new(Guid.CreateVersion7(), "info", "Old news", null, null, DateTimeOffset.UtcNow.AddDays(-2), DateTimeOffset.UtcNow.AddDays(-1));

    public NotificationTests()
    {
        Api.GetUnreadNotificationCountAsync(Arg.Any<CancellationToken>()).Returns(new UnreadCountDto(1));
        Api.GetNotificationsAsync(Arg.Any<bool>(), Arg.Any<PageRequest>(), Arg.Any<CancellationToken>())
            .Returns(c => c.Arg<bool>() ? new PagedResult<NotificationDto>([Unread], 1, 1, 25) : new PagedResult<NotificationDto>([Unread, Read], 2, 1, 25));
        AddAuthorization().SetAuthorized("Ada");
    }

    [Fact]
    public void Localized_notifications_are_translated_and_filled()
    {
        var localized = new NotificationDto(Guid.CreateVersion7(), "account.registration", "New registration", "{0} ({1}) waits for activation.", null,
            DateTimeOffset.UtcNow, null, ["Nia New", "nia@acme.test"]);
        Api.GetNotificationsAsync(Arg.Any<bool>(), Arg.Any<PageRequest>(), Arg.Any<CancellationToken>()).Returns(new PagedResult<NotificationDto>([localized], 1, 1, 25));

        var page = Render<Notifications>();

        page.WaitForAssertion(() => page.Markup.ShouldContain("Nia New (nia@acme.test) waits for activation."));
    }

    [Fact]
    public async Task The_bell_opens_with_the_latest_and_a_link_to_all()
    {
        var popovers = Render<MudBlazor.MudPopoverProvider>();
        var bell = Render<NotificationBell>();

        await bell.Find("button[aria-label='Notifications']").ClickAsync(new());

        popovers.WaitForAssertion(() => popovers.Markup.ShouldContain("A share for you"));
        popovers.Markup.ShouldContain("/notifications");
    }

    [Fact]
    public async Task The_page_lists_all_filters_unread_and_marks_them_read()
    {
        var page = Render<Notifications>();

        page.WaitForAssertion(() => page.FindAll("[data-notification]").Count.ShouldBe(2));
        await page.Find("[data-testid='unread-only'] input, input[data-testid='unread-only']").ChangeAsync(new() { Value = true });
        page.WaitForAssertion(() => page.FindAll("[data-notification]").Count.ShouldBe(1));
        await page.Find($"[data-notification='{Unread.Id}'] [data-testid='mark-read']").ClickAsync(new());
        await page.Find("[data-testid='mark-all']").ClickAsync(new());

        await Api.Received(1).MarkNotificationReadAsync(Unread.Id, Arg.Any<CancellationToken>());
        await Api.Received(1).MarkAllNotificationsReadAsync(Arg.Any<CancellationToken>());
    }
}
