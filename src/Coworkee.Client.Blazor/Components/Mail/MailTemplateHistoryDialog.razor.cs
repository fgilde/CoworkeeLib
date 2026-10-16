using Coworkee.Client.Blazor.Components.Data;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.Mailing;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using MudBlazor.Extensions;

namespace Coworkee.Client.Blazor.Components.Mail;

/// <summary>The saved versions of a template language with a diff to the editor; the chosen one goes back into the editor, unsaved.</summary>
public partial class MailTemplateHistoryDialog
{
    private MailTemplateVersionDto? _selected;

    [CascadingParameter] private IMudDialogInstance Dialog { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Parameter] public IReadOnlyList<MailTemplateVersionDto> Versions { get; set; } = [];

    [Parameter] public string Subject { get; set; } = string.Empty;

    [Parameter] public string Body { get; set; } = string.Empty;

    private const string Pre = "white-space:pre-wrap;word-break:break-word;font-size:12px;border:1px solid var(--mud-palette-lines-default);border-radius:4px;padding:8px;margin:4px 0";

    private static string Style(MailDiffKind kind) => kind switch
    {
        MailDiffKind.Added => "display:block;background:rgba(46,160,67,.18)",
        MailDiffKind.Removed => "display:block;background:rgba(248,81,73,.18);text-decoration:line-through",
        _ => "display:block;color:var(--mud-palette-text-secondary)",
    };

    protected override void OnInitialized() => _selected = Versions.FirstOrDefault();

    /// <summary>Shows the versions; returns the one to restore, or null.</summary>
    public static async Task<MailTemplateVersionDto?> ShowAsync(IDialogService dialogs, CoworkeeLocalizer l, IReadOnlyList<MailTemplateVersionDto> versions, string subject, string body)
    {
        var options = EditDialogExtensions.RightSheet();
        options.MaxWidth = MaxWidth.Large;
        var reference = await dialogs.ShowExAsync<MailTemplateHistoryDialog>(l["Version history"], new DialogParameters<MailTemplateHistoryDialog>
        {
            { d => d.Versions, versions },
            { d => d.Subject, subject },
            { d => d.Body, body },
        }, options);
        return await reference.Result is { Canceled: false, Data: MailTemplateVersionDto version } ? version : null;
    }
}
