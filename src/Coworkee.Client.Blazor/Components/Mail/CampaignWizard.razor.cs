using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Components.Data;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.Mailing;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components.Mail;

/// <summary>Three steps to a campaign: template, recipients, send now or at a time. Closes with <c>true</c> once the campaign is started or scheduled.</summary>
public partial class CampaignWizard
{
    private static readonly string[] Steps = ["Template", "Recipients", "Send"];

    private IReadOnlyList<MailTemplateSummaryDto> _templates = [];
    private RecipientSourceDto? _source;
    private RecipientPreviewDto? _preview;
    private string? _template;
    private string _name = string.Empty;
    private Guid? _id;
    private DateTime? _date;
    private TimeSpan? _time = new(9, 0, 0);
    private int _step;
    private bool _recipientsVisited;
    private bool _busy;

    [CascadingParameter] private IMudDialogInstance Dialog { get; set; } = null!;

    /// <summary>A draft to continue; without one the wizard starts a new campaign.</summary>
    [Parameter] public MailCampaignDto? Campaign { get; set; }

    [Inject] private ICoworkeeApi Api { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [Inject] private IDialogService Dialogs { get; set; } = null!;

    [Inject] private IJSRuntime JS { get; set; } = null!;

    private string? TemplateTitle => _templates.FirstOrDefault(t => t.Name == _template)?.DisplayName ?? _template;

    private bool CanContinue => _step switch
    {
        0 => _template is not null && !string.IsNullOrWhiteSpace(_name),
        1 => _preview is { Count: > 0 },
        _ => false,
    };

    protected override Task OnInitializedAsync()
    {
        if (Campaign is { } draft)
        {
            (_id, _name, _template, _source) = (draft.Id, draft.Name, draft.TemplateName, draft.Recipients);
        }

        return Snackbar.RunAsync(async () => _templates = [.. (await Api.GetMailTemplatesAsync()).Where(t => t.IsCustom)]);
    }

    private void TemplateChanged(string template)
    {
        _template = template;
        if (string.IsNullOrWhiteSpace(_name))
        {
            _name = TemplateTitle ?? string.Empty;
        }
    }

    private void Next()
    {
        _step++;
        _recipientsVisited = true;
    }

    private void Back() => _step--;

    private async Task ScheduleAsync() => await StartAsync(await ScheduledAtAsync());

    // the picked time is wall clock time of the browser, not of the server that renders the page
    private async Task<DateTimeOffset> ScheduledAtAsync()
    {
        var local = DateTime.SpecifyKind(_date!.Value.Date + _time!.Value, DateTimeKind.Unspecified);
        await using var module = await JS.InvokeAsync<IJSObjectReference>("import", "./_content/Coworkee.Client.Blazor/coworkee.js");
        var minutes = await module.InvokeAsync<int>("utcOffsetMinutes", local.Year, local.Month, local.Day, local.Hour, local.Minute);
        return new DateTimeOffset(local, TimeSpan.FromMinutes(minutes));
    }

    private async Task<bool> SaveDraftAsync()
    {
        MailCampaignDto? saved = null;
        if (!await Snackbar.RunAsync(async () => saved = await Api.SaveMailCampaignAsync(_id, new SaveMailCampaignRequest(_name.Trim(), _template!, _source!))))
        {
            return false;
        }

        _id = saved!.Id;
        return true;
    }

    private async Task SendTestAsync()
    {
        _busy = true;
        try
        {
            if (await SaveDraftAsync())
            {
                await Snackbar.RunAsync(() => Api.SendMailCampaignTestAsync(_id!.Value), L["Test mail queued."]);
            }
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task StartAsync(DateTimeOffset? at)
    {
        var count = _preview?.Count ?? 0;
        var question = at is { } when ? L["Schedule \"{0}\" for {2} to {1} recipients?", _name, count, when.ToString("g")] : L["Send \"{0}\" to {1} recipients now?", _name, count];
        if (!await Dialogs.ConfirmAsync(L[at is null ? "Send now" : "Schedule"], question, L["Confirm"], L["Cancel"], Icons.Material.Outlined.Send))
        {
            return;
        }

        _busy = true;
        try
        {
            if (await SaveDraftAsync() && await Snackbar.RunAsync(() => Api.ScheduleMailCampaignAsync(_id!.Value, at?.ToUniversalTime()), L[at is null ? "Campaign started." : "Campaign scheduled."]))
            {
                Dialog.Close(DialogResult.Ok(true));
            }
        }
        finally
        {
            _busy = false;
        }
    }
}
