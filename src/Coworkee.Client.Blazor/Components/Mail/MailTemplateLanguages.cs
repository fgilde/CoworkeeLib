using System.ComponentModel.DataAnnotations;
using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Components.Data;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.Localization;
using Coworkee.Contracts.Mailing;
using MudBlazor;
using MudBlazor.Extensions.Components;
using MudBlazor.Extensions.Components.ObjectEdit;
using MudBlazor.Extensions.Components.ObjectEdit.Options;

namespace Coworkee.Client.Blazor.Components.Mail;

/// <summary>The language variants of a template against the active languages, and the dialog that adds one (copied or machine translated).</summary>
public sealed class MailTemplateLanguages(ICoworkeeApi api, IDialogService dialogs, ISnackbar snackbar, CoworkeeLocalizer l, IReadOnlyList<LanguageDto> languages)
{
    public List<string> Missing(MailTemplateSummaryDto template) =>
        [.. languages.Select(x => x.Culture).Where(c => !template.Cultures.Contains(c, StringComparer.OrdinalIgnoreCase))];

    public string Name(string culture) => languages.FirstOrDefault(x => string.Equals(x.Culture, culture, StringComparison.OrdinalIgnoreCase))?.Name ?? culture;

    /// <summary>Asks for the language and its source; returns the added culture, or null.</summary>
    public async Task<string?> AddAsync(MailTemplateSummaryDto template)
    {
        var missing = Missing(template);
        if (missing.Count == 0)
        {
            return null;
        }

        var form = new LanguageForm { Culture = missing[0], From = SourceOf(template), Translate = true };
        MailTemplateLanguageAddedDto? added = null;
        if (!await dialogs.ShowEditAsync(l["Add language to {0}", template.DisplayName], form,
                async f => added = await api.AddMailTemplateLanguageAsync(template.Name, f.Culture, new(f.From ?? f.Culture, f.Translate)),
                meta => Meta(meta, missing, template.Cultures)) || added is null)
        {
            return null;
        }

        if (template.Cultures.Count > 0)
        {
            snackbar.Add(added.Translated
                ? l["Machine translated from {0}. Please check the text.", Name(added.From)]
                : l["Copied from {0}. Translate the text, or set up a translation service to have it translated.", Name(added.From)], Severity.Info);
        }

        return added.Culture;
    }

    private string? SourceOf(MailTemplateSummaryDto template)
    {
        var preferred = new[] { languages.FirstOrDefault(x => x.IsDefault)?.Culture, l.Culture, CoworkeeLocalizer.English };
        return preferred.FirstOrDefault(c => c is not null && template.Cultures.Contains(c)) ?? template.Cultures.FirstOrDefault();
    }

#pragma warning disable BL0005 // MudEx configures the rendered editors through these instances
    private void Meta(ObjectEditMeta<LanguageForm> meta, List<string> missing, IReadOnlyList<string> existing)
    {
        string Text(string? culture) => culture is null ? string.Empty : $"{LanguageFlags.For(culture)} {Name(culture)} ({culture})";
        meta.Property(f => f.Culture).WithLabel(l["Language"]).RenderWith<MudExSelect<string>, string?>(s => s.Value, s =>
        {
            s.ItemCollection = missing;
            s.ToStringFunc = Text;
        });
        var from = meta.Property(f => f.From).WithLabel(l["Start from"]);
        var translate = meta.Property(f => f.Translate).WithLabel(l["Machine translate (when a translation service is set up)"]);
        if (existing.Count == 0)
        {
            from.Ignore();
            translate.Ignore();
            return;
        }

        from.RenderWith<MudExSelect<string>, string?>(s => s.Value, s =>
        {
            s.ItemCollection = [.. existing];
            s.ToStringFunc = Text;
        });
    }
#pragma warning restore BL0005

    private sealed class LanguageForm
    {
        [Required]
        public string Culture { get; set; } = string.Empty;

        public string? From { get; set; }

        public bool Translate { get; set; }
    }
}
