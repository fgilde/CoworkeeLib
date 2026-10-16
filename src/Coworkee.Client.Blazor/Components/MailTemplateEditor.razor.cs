using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Components.Data;
using Coworkee.Client.Blazor.Components.Mail;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.Mailing;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using MudBlazor;
using MudBlazor.Extensions;

namespace Coworkee.Client.Blazor.Components;

/// <summary>Edits one language of a mail template: tall body editor, live preview sheet, version history and a guard against leaving unsaved changes.</summary>
public partial class MailTemplateEditor : IDisposable
{
    // the page header, the toolbars and the caption take about this much; the body editor and the placeholder list fill the rest
    private const string BodyHeight = "max(320px, calc(100vh - 390px))";
    private const int PreviewDelayMilliseconds = 400;

    private readonly MailPreview _preview = new();
    private MailTemplateDto? _template;
    private string _subject = string.Empty;
    private string _body = string.Empty;
    private List<string> _errors = [];
    private IDialogReference? _sheet;
    private CancellationTokenSource? _debounce;

    [Inject] private ICoworkeeApi Api { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [Inject] private IDialogService Dialogs { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Parameter, EditorRequired] public string Name { get; set; } = string.Empty;

    [Parameter, EditorRequired] public string Culture { get; set; } = string.Empty;

    /// <summary>Subject or body differ from the saved template.</summary>
    public bool Dirty => _template is not null && (_subject != _template.Subject || _body != _template.Body);

    private SaveMailTemplateRequest Content => new(_subject, _body);

    protected override async Task OnParametersSetAsync()
    {
        await LoadAsync();
        await RefreshPreviewAsync(0);
    }

    private async Task LoadAsync()
    {
        _template = await Api.GetMailTemplateAsync(Name, Culture);
        _subject = _template.Subject;
        _body = _template.Body;
        _errors = [];
    }

    private Task SubjectChangedAsync(string? subject)
    {
        _subject = subject ?? string.Empty;
        return RefreshPreviewAsync(PreviewDelayMilliseconds);
    }

    private Task BodyChangedAsync(string body)
    {
        _body = body;
        return RefreshPreviewAsync(PreviewDelayMilliseconds);
    }

    private Task AddToSubjectAsync(MailVariableDto variable) => SubjectChangedAsync(_subject + $"{{{{ {variable.Name} }}}}");

    private async Task TogglePreviewAsync()
    {
        if (_sheet is { } open)
        {
            _sheet = null;
            open.Close();
            return;
        }

        // not modal: the sheet stays open next to the editor and follows every change
        var options = EditDialogExtensions.RightSheet();
        options.Modal = false;
        var sheet = _sheet = await Dialogs.ShowExAsync<MailPreviewSheet>(L["Preview"], new DialogParameters<MailPreviewSheet> { { s => s.Preview, _preview } }, options);
        _ = ForgetWhenClosedAsync(sheet);
        await RefreshPreviewAsync(0);
    }

    private async Task ForgetWhenClosedAsync(IDialogReference sheet)
    {
        await sheet.Result;
        if (_sheet == sheet)
        {
            _sheet = null;
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task RefreshPreviewAsync(int delay)
    {
        if (_sheet is null || _template is null)
        {
            return;
        }

        _debounce?.Cancel();
        var debounce = _debounce = new CancellationTokenSource();
        try
        {
            await Task.Delay(delay, debounce.Token);
            _preview.Show(await Api.PreviewMailTemplateAsync(Name, Culture, Content, debounce.Token), []);
        }
        catch (OperationCanceledException)
        {
        }
        catch (ApiException exception)
        {
            _preview.Show(null, Describe(exception));
        }
    }

    private Task SaveAsync() => RunAsync(async () =>
    {
        await Api.SaveMailTemplateAsync(Name, Culture, Content);
        Snackbar.Add(L["Template saved."], Severity.Success);
        await LoadAsync();
    });

    private Task ResetAsync() => RunAsync(async () =>
    {
        await Api.ResetMailTemplateAsync(Name, Culture);
        await LoadAsync();
        await RefreshPreviewAsync(0);
    });

    private Task SendTestAsync() => RunAsync(async () =>
    {
        await Api.SendTestMailAsync(Name, Culture);
        Snackbar.Add(L["Test mail queued."], Severity.Info);
    });

    private Task HistoryAsync() => RunAsync(async () =>
    {
        var versions = await Api.GetMailTemplateHistoryAsync(Name, Culture);
        if (await MailTemplateHistoryDialog.ShowAsync(Dialogs, L, versions, _subject, _body) is { } version)
        {
            _subject = version.Subject;
            _body = version.Body;
            Snackbar.Add(L["The version is in the editor. Save to keep it."], Severity.Info);
            await RefreshPreviewAsync(0);
        }
    });

    private async Task ConfirmLeaveAsync(LocationChangingContext context)
    {
        if (Dirty && !await Dialogs.ConfirmAsync(L["Unsaved changes"], L["Leave without saving? Your changes to this template are lost."], L["Leave"], L["Stay"],
                Icons.Material.Outlined.EditNote))
        {
            context.PreventNavigation();
        }
    }

    private async Task RunAsync(Func<Task> action)
    {
        _errors = [];
        try
        {
            await action();
        }
        catch (ApiException exception)
        {
            _errors = Describe(exception);
        }
    }

    private static List<string> Describe(ApiException exception) => exception.Errors is { Count: > 0 } errors
        ? errors.SelectMany(e => e.Value).SelectMany(v => v.Split('\n', StringSplitOptions.RemoveEmptyEntries)).ToList()
        : [exception.Message];

    public void Dispose()
    {
        _debounce?.Cancel();
        _debounce?.Dispose();
        _sheet?.Close();
    }
}
