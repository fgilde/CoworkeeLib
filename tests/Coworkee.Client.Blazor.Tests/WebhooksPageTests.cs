using Bunit;
using Coworkee.Client.Blazor.Security;
using Coworkee.Client.Blazor.Webhooks;
using Coworkee.Contracts.Webhooks;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Extensions.Components;
using NSubstitute;

namespace Coworkee.Client.Blazor.Tests;

public sealed class WebhooksPageTests : ClientTestBase
{
    private const string Url = "https://hooks.example.com/in";
    private static readonly Guid WebhookId = Guid.CreateVersion7();
    private static readonly Guid DeliveryId = Guid.CreateVersion7();
    private readonly IWebhooksApi _webhooks = Substitute.For<IWebhooksApi>();
    private readonly IRenderedComponent<MudDialogProvider> _dialogs;

    public WebhooksPageTests()
    {
        Services.AddSingleton(_webhooks);
        AddAuthorization().SetAuthorized("Ada").SetPolicies(PermissionPolicy.For(WebhookPermissions.Manage));
        _webhooks.GetAsync(default).ReturnsForAnyArgs([new WebhookSubscriptionDto(WebhookId, Url, "ERP", ["products.*"], false, 5, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)]);
        _webhooks.GetEventTypesAsync(default).ReturnsForAnyArgs([new WebhookEventTypeDto("products.created", null)]);
        _webhooks.GetDeliveriesAsync(default).ReturnsForAnyArgs([new WebhookDeliveryDto(DeliveryId, WebhookId, Guid.CreateVersion7(), "products.created",
            WebhookDeliveryStatus.Failed, 6, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, 500, 12, "The endpoint answered 500.", "POST " + Url, "HTTP 500")]);
        _webhooks.CreateAsync(default!, default).ReturnsForAnyArgs(new WebhookSecretDto(WebhookId, "whsec_new"));
        Render<MudPopoverProvider>();
        _dialogs = Render<MudDialogProvider>();
    }

    [Fact]
    public void Endpoints_show_their_state_and_deliveries_their_endpoint()
    {
        var page = Render<Pages.Admin.Webhooks>();

        page.WaitForAssertion(() => page.Find($"[data-testid='status-{Url}']").TextContent.Trim().ShouldBe("Switched off"));
        page.Markup.ShouldContain("5 failures in a row");
        page.Find("[data-testid='webhook-deliveries']").TextContent.ShouldContain(Url);
    }

    [Fact]
    public async Task Test_events_and_redeliveries_go_to_the_api()
    {
        var page = Render<Pages.Admin.Webhooks>();

        await page.WaitForElement($"[data-testid='test-{Url}']").ClickAsync(new MouseEventArgs());
        await page.WaitForElement($"[data-testid='redeliver-{DeliveryId}']").ClickAsync(new MouseEventArgs());

        await _webhooks.Received(1).SendTestAsync(WebhookId, Arg.Any<CancellationToken>());
        await _webhooks.Received(1).RedeliverAsync(DeliveryId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_new_endpoint_is_saved_with_its_events_and_shows_its_secret_once()
    {
        var page = Render<Pages.Admin.Webhooks>();
        var creating = page.WaitForElement("[data-testid='create-webhook']").ClickAsync(new MouseEventArgs());
        _dialogs.WaitForAssertion(() => _dialogs.FindComponents<MudExTagField<string>>().Count.ShouldBe(1));

        await _dialogs.InvokeAsync(() => InputByLabel(_dialogs.Find("form"), "Address").Change("https://erp.example.com/hooks"));
        var events = _dialogs.FindComponent<MudExTagField<string>>();
        await events.Find("input").InputAsync(new ChangeEventArgs { Value = "products.created" });
        await events.Find("input").BlurAsync(new FocusEventArgs());
        await _dialogs.Find("form").SubmitAsync();

        _dialogs.WaitForAssertion(() => _dialogs.Find("[data-testid='webhook-secret']").TextContent.ShouldBe("whsec_new"));
        await _webhooks.Received(1).CreateAsync(Arg.Is<WebhookRequest>(r =>
            r.Url == "https://erp.example.com/hooks" && r.Events.SequenceEqual(new[] { "products.created" }) && r.IsActive), Arg.Any<CancellationToken>());
        creating.IsCompleted.ShouldBeFalse();
    }

    [Fact]
    public async Task Rotating_asks_first_shows_the_new_secret_once_and_the_grace_period()
    {
        _webhooks.RotateSecretAsync(WebhookId, Arg.Any<CancellationToken>()).Returns(new WebhookSecretDto(WebhookId, "whsec_rotated"));
        var page = Render<Pages.Admin.Webhooks>();

        var rotating = page.WaitForElement($"[data-testid='rotate-{Url}']").ClickAsync(new MouseEventArgs());
        _dialogs.WaitForAssertion(() => _dialogs.FindAll("button").Any(b => b.TextContent.Trim() == "Rotate secret").ShouldBeTrue());
        await _dialogs.FindAll("button").Last(b => b.TextContent.Trim() == "Rotate secret").ClickAsync(new MouseEventArgs());

        _dialogs.WaitForAssertion(() => _dialogs.Find("[data-testid='webhook-secret']").TextContent.ShouldBe("whsec_rotated"));
        await _webhooks.Received(1).RotateSecretAsync(WebhookId, Arg.Any<CancellationToken>());
        rotating.IsCompleted.ShouldBeFalse();

        _webhooks.GetAsync(default).ReturnsForAnyArgs([new WebhookSubscriptionDto(WebhookId, Url, "ERP", ["products.*"], true, 0, null, DateTimeOffset.UtcNow)
            { PreviousSecretUntil = DateTimeOffset.UtcNow.AddHours(24) }]);
        var later = Render<Pages.Admin.Webhooks>();
        later.WaitForAssertion(() => later.Find($"[data-testid='previous-{Url}']").TextContent.ShouldContain("Old secret signs too until"));
    }
}
