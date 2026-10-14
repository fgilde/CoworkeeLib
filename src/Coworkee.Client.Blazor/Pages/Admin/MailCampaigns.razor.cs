using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Components.Data;
using Coworkee.Client.Blazor.Components.Mail;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.Mailing;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Pages.Admin;

public partial class MailCampaigns : IDisposable
{
    private IReadOnlyList<MailCampaignDto> _campaigns = [];
    private Timer? _timer;
    private bool _busy;

    [Inject] private ICoworkeeApi Api { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Inject] private IDialogService Dialogs { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    /// <summary>How often the list reloads while a campaign is sending.</summary>
    [Parameter] public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

    protected override async Task OnInitializedAsync()
    {
        await LoadAsync();
        _timer = new Timer(_ => _ = InvokeAsync(PollAsync), null, PollInterval, PollInterval);
    }

    private static Color ColorFor(MailCampaignStatus status) => status switch
    {
        MailCampaignStatus.Sent => Color.Success,
        MailCampaignStatus.Failed => Color.Error,
        MailCampaignStatus.Sending or MailCampaignStatus.Scheduled => Color.Info,
        _ => Color.Default,
    };

    private static DateTimeOffset? When(MailCampaignDto campaign) =>
        campaign.Status == MailCampaignStatus.Scheduled ? campaign.ScheduledFor : campaign.FinishedAt ?? campaign.StartedAt ?? campaign.CreatedAt;

    private Task LoadAsync() => Snackbar.RunAsync(async () => _campaigns = await Api.GetMailCampaignsAsync());

    private async Task PollAsync()
    {
        if (_campaigns.Any(c => c.Status == MailCampaignStatus.Sending))
        {
            await LoadAsync();
            StateHasChanged();
        }
    }

    private async Task CreateAsync()
    {
        var dialog = await Dialogs.ShowSideSheetAsync<CampaignWizard>(L["New campaign"], new DialogParameters(), o => o.MaxWidth = MaxWidth.Large);
        await dialog.Result;
        await LoadAsync();
    }

    private async Task CancelAsync(MailCampaignDto campaign)
    {
        if (await Dialogs.ConfirmAsync(L["Cancel campaign"], L["Cancel {0}? Mails not sent yet will not go out.", campaign.Name], L["Cancel campaign"], L["Close"], Icons.Material.Outlined.Cancel))
        {
            await RunAsync(() => Api.CancelMailCampaignAsync(campaign.Id));
        }
    }

    private async Task DeleteAsync(MailCampaignDto campaign)
    {
        if (await Dialogs.ConfirmAsync(L["Delete"], L["Delete {0}? This cannot be undone.", campaign.Name], L["Delete"], L["Cancel"], Icons.Material.Outlined.DeleteForever))
        {
            await RunAsync(() => Api.DeleteMailCampaignAsync(campaign.Id));
        }
    }

    private async Task RunAsync(Func<Task> action)
    {
        _busy = true;
        try
        {
            await Snackbar.RunAsync(action);
            await LoadAsync();
        }
        finally
        {
            _busy = false;
        }
    }

    public void Dispose() => _timer?.Dispose();
}
