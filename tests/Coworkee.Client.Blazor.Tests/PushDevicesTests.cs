using Bunit;
using Coworkee.Client.Blazor.Components;
using Coworkee.Contracts.Notifications;
using NSubstitute;

namespace Coworkee.Client.Blazor.Tests;

public sealed class PushDevicesTests : ClientTestBase
{
    [Fact]
    public void Nothing_shows_while_push_is_not_configured()
    {
        Api.GetPushSettingsAsync(Arg.Any<CancellationToken>()).Returns(new PushSettingsDto(null, []));

        var cut = Render<PushDevices>();

        cut.FindAll("[data-testid='push-settings']").ShouldBeEmpty();
    }

    [Fact]
    public void The_devices_are_listed_and_a_browser_without_push_cannot_switch_it_on()
    {
        Api.GetPushSettingsAsync(Arg.Any<CancellationToken>()).Returns(new PushSettingsDto("key",
            [new PushDeviceDto(Guid.NewGuid(), "https://push.example.com/1", "Firefox · Linux", DateTimeOffset.UtcNow)]));

        var cut = Render<PushDevices>();

        cut.WaitForAssertion(() => cut.Find("[data-testid='push-settings']").TextContent.ShouldContain("Firefox · Linux"));
        cut.Markup.ShouldContain("This browser cannot receive push notifications.");
        cut.Find("input[data-testid='push-toggle'], [data-testid='push-toggle'] input").HasAttribute("disabled").ShouldBeTrue();
    }
}
