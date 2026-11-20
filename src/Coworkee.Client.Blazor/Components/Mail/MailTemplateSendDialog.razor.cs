using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Components.Data;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Mailing;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using MudBlazor.Extensions;

namespace Coworkee.Client.Blazor.Components.Mail;

/// <summary>
/// Sends a mail template for real: recipients from a <see cref="Users.CoworkeeUserSelection"/>, the template's values in a MudEx structured data editor
/// (started from its sample), a preview of the first recipient's mail, then one mail per recipient through the mail log.
/// </summary>
public partial class MailTemplateSendDialog
{
    private UserSelection _recipients = new();
    private string? _model;
    private MailTemplateSendPreviewDto? _preview;
    private bool _busy;

    [CascadingParameter] private IMudDialogInstance Dialog { get; set; } = null!;

    [Inject] private ICoworkeeApi Api { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Parameter] public string Name { get; set; } = string.Empty;

    /// <summary>The template's sample model as JSON (<see cref="MailTemplateDto.SampleModel"/>).</summary>
    [Parameter] public string? SampleModel { get; set; }

    private SendMailTemplateRequest Request => new(_recipients, string.IsNullOrWhiteSpace(_model) ? null : _model);

    protected override void OnInitialized() => _model = SampleModel;

    // a changed choice or value needs a new preview before it is sent
    private void Forget() => _preview = null;

    private async Task PreviewAsync()
    {
        _busy = true;
        await Snackbar.RunAsync(async () => _preview = await Api.PreviewMailTemplateSendAsync(Name, Request));
        _busy = false;
    }

    private async Task SendAsync()
    {
        _busy = true;
        var queued = 0;
        if (await Snackbar.RunAsync(async () => queued = await Api.SendMailTemplateAsync(Name, Request)))
        {
            Snackbar.Add(L["{0} mails queued", queued], Severity.Success);
            Dialog.Close(DialogResult.Ok(queued));
        }

        _busy = false;
    }

    /// <summary>Opens the dialog for <paramref name="template"/>; returns how many mails were queued, null when cancelled.</summary>
    public static async Task<int?> ShowAsync(IDialogService dialogs, CoworkeeLocalizer l, MailTemplateDto template)
    {
        var options = EditDialogExtensions.RightSheet();
        options.MaxWidth = MaxWidth.Large;
        var reference = await dialogs.ShowExAsync<MailTemplateSendDialog>(l["Send {0}", template.Name], new DialogParameters<MailTemplateSendDialog>
        {
            { d => d.Name, template.Name },
            { d => d.SampleModel, template.SampleModel },
        }, options);
        return await reference.Result is { Canceled: false, Data: int queued } ? queued : null;
    }
}
