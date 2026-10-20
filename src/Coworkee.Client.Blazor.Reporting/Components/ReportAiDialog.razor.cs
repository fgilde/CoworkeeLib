using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Client.Blazor.Reporting.Api;
using Coworkee.Client.Blazor.Reporting.State;
using Coworkee.Contracts.Reporting;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Reporting.Components;

/// <summary>Asks the server's AI for a new report or a change of <see cref="Current"/>; closes with the checked, unsaved definition.</summary>
public partial class ReportAiDialog
{
    private IReadOnlyList<string> _examples = [];
    private string? _prompt;
    private string? _error;
    private bool _busy;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Inject] private IReportsApi Api { get; set; } = null!;

    [CascadingParameter] private IMudDialogInstance Dialog { get; set; } = null!;

    /// <summary>The report to change; null builds a new one.</summary>
    [Parameter] public ReportDefinition? Current { get; set; }

    protected override async Task OnInitializedAsync()
    {
        try
        {
            _examples = ReportPromptExamples.For(await Api.GetSourcesAsync(), L);
        }
        catch (ApiException)
        {
        }
    }

    private async Task GenerateAsync()
    {
        _busy = true;
        _error = null;
        try
        {
            Dialog.Close(DialogResult.Ok(await Api.GenerateAsync(new GenerateReportRequest(_prompt!.Trim(), Current))));
        }
        catch (ApiException exception)
        {
            _error = exception.Status == 403 ? L["No AI model is set up, or you may not create reports."]
                : exception.Errors is { Count: > 0 } errors ? string.Join(" ", errors.SelectMany(e => e.Value))
                : L["That did not work: {0}", exception.Message];
        }
        catch (HttpRequestException)
        {
            _error = L["The AI could not be reached. Try again later."];
        }
        finally
        {
            _busy = false;
        }
    }

    public static async Task<ReportDefinition?> ShowAsync(IDialogService dialogs, string title, ReportDefinition? current)
    {
        var dialog = await dialogs.ShowAsync<ReportAiDialog>(title, new DialogParameters<ReportAiDialog> { { d => d.Current, current } },
            new DialogOptions { MaxWidth = MaxWidth.Small, FullWidth = true, CloseOnEscapeKey = true });
        var result = await dialog.Result;
        return result is { Canceled: false, Data: ReportDefinition definition } ? definition : null;
    }
}
