using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Components.Data;
using Coworkee.Client.Blazor.Components.Editors;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Client.Blazor.Webhooks;
using Coworkee.Contracts.Webhooks;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using MudBlazor.Extensions.Components.ObjectEdit;
using MudBlazor.Extensions.Components.ObjectEdit.Options;

namespace Coworkee.Client.Blazor.Pages.Admin;

public partial class Webhooks
{
    private IReadOnlyList<WebhookSubscriptionDto> _webhooks = [];
    private IReadOnlyList<WebhookDeliveryDto> _deliveries = [];
    private IReadOnlyList<string> _eventTypes = [];
    private bool _busy;

    [Inject] private IWebhooksApi Api { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Inject] private IDialogService Dialogs { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    protected override async Task OnInitializedAsync()
    {
        await Snackbar.RunAsync(async () => _eventTypes = [WebhookEventTypes.All, .. (await Api.GetEventTypesAsync()).Select(t => t.Name)]);
        await RunAsync(() => Task.CompletedTask);
    }

    private async Task CreateAsync()
    {
        WebhookSecretDto? created = null;
        if (await Dialogs.ShowEditAsync(L["Add endpoint"], new WebhookForm(), async form => created = await Api.CreateAsync(form.ToRequest()), Meta))
        {
            await RunAsync(() => Task.CompletedTask);
            if (created is not null)
            {
                await ShowSecretAsync(L["Copy the secret now, it is not shown again:"], created.Secret);
            }
        }
    }

    private async Task EditAsync(WebhookSubscriptionDto webhook)
    {
        var form = new WebhookForm { Url = webhook.Url, Description = webhook.Description, Events = webhook.Events, IsActive = webhook.IsActive };
        if (await Dialogs.ShowEditAsync(L["Edit endpoint"], form, f => Api.UpdateAsync(webhook.Id, f.ToRequest()), Meta))
        {
            await RunAsync(() => Task.CompletedTask);
        }
    }

#pragma warning disable BL0005 // MudEx configures the rendered editor through this instance
    private void Meta(ObjectEditMeta<WebhookForm> meta)
    {
        meta.Property(f => f.Url).WithLabel(L["Address"]);
        meta.Property(f => f.Description).WithLabel(L["Description"]);
        meta.Property(f => f.IsActive).WithLabel(L["Active"]);
        meta.Property(f => f.Events).RenderAsChips(e =>
        {
            e.Label = L["Events"];
            e.HelperText = L["e.g. products.created, products.* or * for everything"];
            e.SearchFunc = (text, _) => Task.FromResult(_eventTypes.Where(t => t.Contains(text, StringComparison.OrdinalIgnoreCase)));
        });
    }
#pragma warning restore BL0005

    private async Task DeleteAsync(WebhookSubscriptionDto webhook)
    {
        if (await Dialogs.ConfirmAsync(L["Delete"], L["Delete {0}? This cannot be undone.", webhook.Url], L["Delete"], L["Cancel"], Icons.Material.Outlined.DeleteForever))
        {
            await RunAsync(() => Api.DeleteAsync(webhook.Id));
        }
    }

    private Task SendTestAsync(WebhookSubscriptionDto webhook) => RunAsync(() => Api.SendTestAsync(webhook.Id), L["Test event queued"]);

    private Task RedeliverAsync(WebhookDeliveryDto delivery) => RunAsync(() => Api.RedeliverAsync(delivery.Id), L["Queued again"]);

    private async Task ShowSecretAsync(WebhookSubscriptionDto webhook)
    {
        WebhookSecretDto? secret = null;
        if (await Snackbar.RunAsync(async () => secret = await Api.GetSecretAsync(webhook.Id)) && secret is not null)
        {
            await ShowSecretAsync(L["Check the webhook-signature header with this secret:"], secret.Secret);
        }
    }

    private Task ShowSecretAsync(string text, string secret) =>
        Dialogs.ShowMessageBoxAsync(L["Signing secret"],
            (MarkupString)$"<p>{System.Net.WebUtility.HtmlEncode(text)}</p><code data-testid=\"webhook-secret\">{System.Net.WebUtility.HtmlEncode(secret)}</code>");

    private string UrlOf(Guid subscriptionId) => _webhooks.FirstOrDefault(w => w.Id == subscriptionId)?.Url ?? string.Empty;

    private static Color StatusColor(WebhookDeliveryStatus status) => status switch
    {
        WebhookDeliveryStatus.Succeeded => Color.Success,
        WebhookDeliveryStatus.Failed => Color.Error,
        _ => Color.Default,
    };

    private async Task RunAsync(Func<Task> action, string? success = null)
    {
        _busy = true;
        try
        {
            await Snackbar.RunAsync(action, success);
            await Snackbar.RunAsync(async () =>
            {
                _webhooks = await Api.GetAsync();
                _deliveries = await Api.GetDeliveriesAsync();
            });
        }
        finally
        {
            _busy = false;
        }
    }

    private sealed class WebhookForm
    {
        public string Url { get; set; } = "https://";

        public string? Description { get; set; }

        public IReadOnlyList<string> Events { get; set; } = [];

        public bool IsActive { get; set; } = true;

        public WebhookRequest ToRequest() => new(Url, Events, Description, IsActive);
    }
}
