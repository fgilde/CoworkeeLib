using System.Globalization;
using System.Text.Json.Nodes;
using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Components.Data;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Mailing;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using MudBlazor.Extensions;
using MudBlazor.Extensions.Components.ObjectEdit;
using MudBlazor.Extensions.Components.ObjectEdit.Options;
using Nextended.Core.Helper;

namespace Coworkee.Client.Blazor.Components.Mail;

/// <summary>
/// Sends a mail template for real, as a wizard: the recipients from a <see cref="Users.CoworkeeUserSelection"/> (facets, roles, groups, single users),
/// only the values the template reads (<see cref="MailTemplateDto.SendModel"/>) in a MudEx form built from them (labels, groups, samples; JSON on request),
/// then the mail of each recipient to page through and one mail per recipient through the mail log.
/// </summary>
public partial class MailTemplateSendDialog
{
    /// <summary>The most recipients one send takes (the server's limit; more go through a campaign).</summary>
    private const int MaxRecipients = 1000;

    private static readonly RenderFragment<MudStepper> NoActions = _ => _ => { };
    private UserSelection _recipients = new();
    private UserSelectionCountDto? _count;
    private string? _model;
    private bool _json;
    private int _step;
    private int _index;
    private MailTemplateSendPreviewDto? _preview;
    private bool _busy;

    [CascadingParameter] private IMudDialogInstance Dialog { get; set; } = null!;

    [Inject] private ICoworkeeApi Api { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Parameter] public string Name { get; set; } = string.Empty;

    /// <summary>The values a send asks for as JSON (<see cref="MailTemplateDto.SendModel"/>, else the whole <see cref="MailTemplateDto.SampleModel"/>).</summary>
    [Parameter] public string? SampleModel { get; set; }

    /// <summary>The template's variables (<see cref="MailTemplateDto.Variables"/>): type and sample of each field.</summary>
    [Parameter] public IReadOnlyList<MailVariableDto> Variables { get; set; } = [];

    private SendMailTemplateRequest Request => new(_recipients, string.IsNullOrWhiteSpace(_model) ? null : _model);

    private bool HasRecipients => !_recipients.IsEmpty && _count is not { Active: 0 };

    private bool HasValues => ValueCount > 0;

    private int ValueCount => Parse(SampleModel) is JsonObject root ? Leaves(root) : 0;

    private string? ValuesText => _step < 1 ? null : HasValues ? L["{0} values", ValueCount] : L["None needed"];

    // the preview pages through the recipients a send reaches, at most as many as one send takes
    private int LastIndex => Math.Max(0, Math.Min(_preview?.Recipients ?? 0, MaxRecipients) - 1);

    protected override void OnInitialized() => _model = SampleModel;

    private void CountChanged(UserSelectionCountDto count) => _count = count;

    // a changed choice or value needs a new preview before it is sent
    private void Forget() => _preview = null;

    private async Task GoToAsync(int step)
    {
        if (step < 0 || step > 2 || (step > 0 && !HasRecipients))
        {
            return;
        }

        _step = step;
        if (step == 2 && _preview is null)
        {
            await ShowAsync(0);
        }
    }

    private async Task ShowAsync(int index)
    {
        _busy = true;
        var shown = await Snackbar.RunAsync(async () => _preview = await Api.PreviewMailTemplateSendAsync(Name, Request, index));
        if (shown)
        {
            _index = Math.Min(index, LastIndex);
        }

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

    // the form of the values: a readable label for each field, a group for each object, the template's sample as the hint
    private void Configure(ObjectEditMeta<IStructuredDataObject> meta)
    {
        foreach (var property in meta.AllProperties)
        {
            var path = property.PropertyName;
            var segments = path.Split('.');
            property.WithLabel(Label(segments[^1]));
            if (segments.Length > 1)
            {
                property.WithGroup(string.Join(" / ", segments.SkipLast(1).Select(Label)));
            }

            if (Variables.FirstOrDefault(v => v.Name == path) is { Sample: { Length: > 0 } sample })
            {
                property.WithDescription(L["e.g. {0}", sample]);
            }
        }
    }

    /// <summary>"port_name" as "Port name".</summary>
    internal static string Label(string name)
    {
        var words = name.Replace('_', ' ').Trim();
        return words.Length == 0 ? name : char.ToUpper(words[0], CultureInfo.CurrentCulture) + words[1..];
    }

    private static JsonNode? Parse(string? json)
    {
        try
        {
            return string.IsNullOrWhiteSpace(json) ? null : JsonNode.Parse(json);
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    // the fields of the form: an empty object has none, an empty list is one
    private static int Leaves(JsonNode? node) => node switch
    {
        JsonObject o => o.Sum(p => Leaves(p.Value)),
        JsonArray { Count: > 0 } a => a.Sum(Leaves),
        _ => 1,
    };

    /// <summary>Opens the wizard for <paramref name="template"/>; returns how many mails were queued, null when cancelled.</summary>
    public static async Task<int?> ShowAsync(IDialogService dialogs, CoworkeeLocalizer l, MailTemplateDto template)
    {
        var options = EditDialogExtensions.RightSheet();
        options.MaxWidth = MaxWidth.Large;
        var reference = await dialogs.ShowExAsync<MailTemplateSendDialog>(l["Send {0}", template.Name], new DialogParameters<MailTemplateSendDialog>
        {
            { d => d.Name, template.Name },
            { d => d.SampleModel, template.SendModel ?? template.SampleModel },
            { d => d.Variables, template.Variables },
        }, options);
        return await reference.Result is { Canceled: false, Data: int queued } ? queued : null;
    }
}
