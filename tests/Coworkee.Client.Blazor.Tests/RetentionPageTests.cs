using Bunit;
using Coworkee.Client.Blazor.Retention;
using Coworkee.Contracts.Retention;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using NSubstitute;

namespace Coworkee.Client.Blazor.Tests;

public sealed class RetentionPageTests : ClientTestBase
{
    private readonly IRetentionApi _api = Substitute.For<IRetentionApi>();

    public RetentionPageTests()
    {
        Services.AddSingleton(_api);
        AddAuthorization().SetAuthorized("Ada").SetPolicies(Security.PermissionPolicy.For(RetentionPermissions.Manage));
        Pages.Admin.Retention.CountDelay = TimeSpan.FromMilliseconds(10);
        Pages.Admin.Retention.PollInterval = TimeSpan.FromMilliseconds(20);
    }

    private static RetentionTargetDto Target(string key, int? days = null, long? affected = null, bool running = false, long? deleted = null) =>
        new(key, $"Title of {key}", null, days, key == "webhook-deliveries" ? 30 : null, affected, deleted is null ? null : DateTimeOffset.UtcNow, deleted, null, running);

    [Fact]
    public async Task Typed_days_are_counted_and_saved()
    {
        _api.GetAsync(default, default).ReturnsForAnyArgs([Target("audit"), Target("webhook-deliveries", 30, 4)]);
        _api.CountAsync("audit", 365, Arg.Any<CancellationToken>()).Returns(12480);
        _api.CountAsync("webhook-deliveries", 60, Arg.Any<CancellationToken>()).Returns(2);
        var page = Render<Pages.Admin.Retention>();
        page.WaitForAssertion(() => page.Markup.ShouldContain("Title of audit"));
        page.Find("[data-affected='webhook-deliveries']").TextContent.ShouldBe(4.ToString("N0"));

        await page.InvokeAsync(() => page.Find("[data-key='audit'] input, input[data-key='audit']").Change("365"));

        page.WaitForAssertion(() => page.Find("[data-affected='audit']").TextContent.ShouldBe(12480.ToString("N0")));
        page.Find("button[data-delete='webhook-deliveries']").HasAttribute("disabled").ShouldBeFalse();
        await page.InvokeAsync(() => page.Find("[data-key='webhook-deliveries'] input, input[data-key='webhook-deliveries']").Change("60"));
        page.WaitForAssertion(() => page.Find("button[data-delete='webhook-deliveries']").HasAttribute("disabled").ShouldBeTrue("an unsaved value is never deleted with"));
        await page.Find("[data-testid='save-retention']").ClickAsync(new());
        await _api.Received(1).SetDaysAsync(Arg.Is<IReadOnlyDictionary<string, int?>>(d => d.Count == 2 && d["audit"] == 365 && d["webhook-deliveries"] == 60), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Delete_now_names_the_count_and_follows_the_run_until_it_is_done()
    {
        var calls = 0;
        _api.GetAsync(default, default).ReturnsForAnyArgs(_ => ++calls switch
        {
            1 => [Target("notifications", 365, 12480)],
            2 => [Target("notifications", 365, 12480, running: true)],
            _ => [Target("notifications", 365, 0, deleted: 12480)],
        });
        _api.CountAsync("notifications", 365, Arg.Any<CancellationToken>()).Returns(12480);
        var dialogs = Render<MudDialogProvider>();
        Render<MudPopoverProvider>();
        var page = Render<Pages.Admin.Retention>();
        page.WaitForAssertion(() => page.Markup.ShouldContain("Title of notifications"));

        var deleting = page.Find("[data-delete='notifications']").ClickAsync(new());
        dialogs.WaitForAssertion(() => dialogs.Markup.ShouldContain(12480.ToString("N0")));
        await dialogs.FindAll("button").First(b => b.TextContent.Trim() == "Delete").ClickAsync(new());
        await deleting;

        await _api.Received(1).RunAsync("notifications", 365, Arg.Any<CancellationToken>());
        page.WaitForAssertion(() =>
        {
            page.FindAll("[data-running='notifications']").ShouldBeEmpty();
            page.Markup.ShouldContain(12480.ToString("N0") + " deleted");
        });
        calls.ShouldBeGreaterThanOrEqualTo(4);
        await _api.Received().GetAsync(false, Arg.Any<CancellationToken>());
    }
}
