using Coworkee.Client.Blazor.Api;
using Coworkee.Contracts.Mailing;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components;

public partial class MailTemplateEditor
{
    [Inject] private ICoworkeeApi Api { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    private MailTemplateDto? _template;
    private string _subject = string.Empty;
    private string _body = string.Empty;
    private RenderedMailDto? _preview;
    private List<string> _errors = [];

    [Parameter, EditorRequired] public string Name { get; set; } = string.Empty;

    [Parameter, EditorRequired] public string Culture { get; set; } = string.Empty;

    protected override async Task OnParametersSetAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        _template = await Api.GetMailTemplateAsync(Name, Culture);
        _subject = _template.Subject;
        _body = _template.Body;
        _errors = [];
    }

    private SaveMailTemplateRequest Content => new(_subject, _body);

    private Task PreviewAsync() => RunAsync(async () => _preview = await Api.PreviewMailTemplateAsync(Name, Culture, Content));

    private Task SaveAsync() => RunAsync(async () =>
    {
        await Api.SaveMailTemplateAsync(Name, Culture, Content);
        Snackbar.Add("Template saved.", Severity.Success);
        await LoadAsync();
    });

    private Task ResetAsync() => RunAsync(async () =>
    {
        await Api.ResetMailTemplateAsync(Name, Culture);
        await LoadAsync();
    });

    private Task SendTestAsync() => RunAsync(async () =>
    {
        await Api.SendTestMailAsync(Name, Culture);
        Snackbar.Add("Test mail queued.", Severity.Info);
    });

    private async Task RunAsync(Func<Task> action)
    {
        _errors = [];
        try
        {
            await action();
        }
        catch (ApiException exception)
        {
            _errors = exception.Errors is { Count: > 0 } errors
                ? errors.SelectMany(e => e.Value).SelectMany(v => v.Split('\n', StringSplitOptions.RemoveEmptyEntries)).ToList()
                : [exception.Message];
        }
    }
}
