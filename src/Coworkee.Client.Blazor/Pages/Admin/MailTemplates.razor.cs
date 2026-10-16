using System.ComponentModel.DataAnnotations;
using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Components.Data;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Client.Blazor.Security;
using Coworkee.Contracts.Localization;
using Coworkee.Contracts.Mailing;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using MudBlazor;
using MudBlazor.Extensions.Components;
using MudBlazor.Extensions.Components.ObjectEdit;
using MudBlazor.Extensions.Components.ObjectEdit.Options;

namespace Coworkee.Client.Blazor.Pages.Admin;

public partial class MailTemplates
{
    private IReadOnlyList<MailTemplateSummaryDto> _templates = [];
    private IReadOnlyList<LanguageDto> _languages = [];
    private bool _system = true;
    private bool _busy;

    [Inject] private ICoworkeeApi Api { get; set; } = null!;

    [Inject] private ILocalizationApi Localization { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Inject] private IDialogService Dialogs { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [Inject] private Navigation.CoworkeePages Links { get; set; } = null!;

    [Inject] private NavigationManager Navigation { get; set; } = null!;

    [CascadingParameter] private Task<AuthenticationState>? AuthenticationState { get; set; }

    protected override async Task OnInitializedAsync()
    {
        _system = AuthenticationState is null || !SystemTenant.IsOutside((await AuthenticationState).User);
        await LoadAsync();
    }

    private Task LoadAsync() => Snackbar.RunAsync(async () =>
    {
        _templates = await Api.GetMailTemplatesAsync();
        _languages = await Localization.GetLanguagesAsync();
    });

    private Task CreateAsync() => CreateAsync(new TemplateForm());

    private Task DuplicateAsync(MailTemplateSummaryDto template) => CreateAsync(new TemplateForm
    {
        Name = CopyName(template.Name),
        DisplayName = L["{0} (copy)", template.DisplayName],
        Description = template.Description,
        CopyFrom = template.Name,
    });

    private string CopyName(string name)
    {
        var candidate = $"{name}.copy";
        for (var n = 2; _templates.Any(t => t.Name == candidate); n++)
        {
            candidate = $"{name}.copy{n}";
        }

        return candidate;
    }

    private async Task CreateAsync(TemplateForm model)
    {
        var created = string.Empty;
        if (await Dialogs.ShowEditAsync(L["New mail template"], model, async form =>
            {
                await Api.CreateMailTemplateAsync(form.ToRequest());
                created = form.Name;
            }, Meta))
        {
            Navigation.NavigateTo(Links.Href($"/admin/mail/templates/{Uri.EscapeDataString(created)}/{L.Culture}")!);
        }
    }

    private async Task DeleteAsync(MailTemplateSummaryDto template)
    {
        if (!await Dialogs.ConfirmAsync(L["Delete"], L["Delete {0}? This cannot be undone.", template.DisplayName], L["Delete"], L["Cancel"], Icons.Material.Outlined.DeleteForever))
        {
            return;
        }

        _busy = true;
        try
        {
            await Snackbar.RunAsync(() => Api.DeleteMailTemplateAsync(template.Name));
            await LoadAsync();
        }
        finally
        {
            _busy = false;
        }
    }

    private List<string> Missing(MailTemplateSummaryDto template) =>
        [.. _languages.Select(l => l.Culture).Where(c => !template.Cultures.Contains(c, StringComparer.OrdinalIgnoreCase))];

    private string LanguageName(string culture) => _languages.FirstOrDefault(l => string.Equals(l.Culture, culture, StringComparison.OrdinalIgnoreCase))?.Name ?? culture;

    // a shipped default can only be reset in the editor; the last variant of an own template stays
    private EventCallback<MudChip<string>> RemoveHandler(MailTemplateSummaryDto template, string culture) =>
        template.OverriddenCultures.Contains(culture) && !(template.DefaultCultures ?? []).Contains(culture) && (!template.IsCustom || template.Cultures.Count > 1)
            ? EventCallback.Factory.Create<MudChip<string>>(this, () => RemoveLanguageAsync(template, culture))
            : default;

    private async Task AddLanguageAsync(MailTemplateSummaryDto template)
    {
        var missing = Missing(template);
        var form = new LanguageForm { Culture = missing[0], From = SourceOf(template), Translate = true };
        MailTemplateLanguageAddedDto? added = null;
        if (!await Dialogs.ShowEditAsync(L["Add language to {0}", template.DisplayName], form,
                async f => added = await Api.AddMailTemplateLanguageAsync(template.Name, f.Culture, new(f.From ?? f.Culture, f.Translate)),
                meta => LanguageMeta(meta, missing, template.Cultures)) || added is null)
        {
            return;
        }

        if (template.Cultures.Count > 0)
        {
            Snackbar.Add(added.Translated
                ? L["Machine translated from {0}. Please check the text.", LanguageName(added.From)]
                : L["Copied from {0}. Translate the text, or set up a translation service to have it translated.", LanguageName(added.From)], Severity.Info);
        }

        Open(template, added.Culture);
    }

    // a chip with Href renders as a link without its close button, so the chips navigate on click
    private void Open(MailTemplateSummaryDto template, string culture) => Navigation.NavigateTo(Links.Href($"/admin/mail/templates/{Uri.EscapeDataString(template.Name)}/{culture}")!);

    private string? SourceOf(MailTemplateSummaryDto template)
    {
        var preferred = new[] { _languages.FirstOrDefault(l => l.IsDefault)?.Culture, L.Culture, CoworkeeLocalizer.English };
        return preferred.FirstOrDefault(c => c is not null && template.Cultures.Contains(c)) ?? template.Cultures.FirstOrDefault();
    }

    private async Task RemoveLanguageAsync(MailTemplateSummaryDto template, string culture)
    {
        if (!await Dialogs.ConfirmAsync(L["Remove language"], L["Remove the {0} variant of {1}? Its mails then use the default language.", LanguageName(culture), template.DisplayName],
                L["Remove"], L["Cancel"], Icons.Material.Outlined.Delete))
        {
            return;
        }

        _busy = true;
        try
        {
            await Snackbar.RunAsync(() => Api.ResetMailTemplateAsync(template.Name, culture));
            await LoadAsync();
        }
        finally
        {
            _busy = false;
        }
    }

#pragma warning disable BL0005 // MudEx configures the rendered editors through these instances
    private void LanguageMeta(ObjectEditMeta<LanguageForm> meta, List<string> missing, IReadOnlyList<string> existing)
    {
        string Text(string? culture) => culture is null ? string.Empty : $"{LanguageFlags.For(culture)} {LanguageName(culture)} ({culture})";
        meta.Property(f => f.Culture).WithLabel(L["Language"]).RenderWith<MudExSelect<string>, string?>(s => s.Value, s =>
        {
            s.ItemCollection = missing;
            s.ToStringFunc = Text;
        });
        var from = meta.Property(f => f.From).WithLabel(L["Start from"]);
        var translate = meta.Property(f => f.Translate).WithLabel(L["Machine translate (when a translation service is set up)"]);
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

    private void Meta(ObjectEditMeta<TemplateForm> meta)
    {
        meta.Property(f => f.Name).WithLabel(L["Name"]);
        meta.Property(f => f.DisplayName).WithLabel(L["Display name"]);
        meta.Property(f => f.Description).WithLabel(L["Description"]);
        meta.Property(f => f.CopyFrom).Ignore();
        var global = meta.Property(f => f.Global).WithLabel(L["Global"]);
        if (!_system)
        {
            global.Ignore();
        }
    }

    private sealed class LanguageForm
    {
        [Required]
        public string Culture { get; set; } = string.Empty;

        public string? From { get; set; }

        public bool Translate { get; set; }
    }

    private sealed class TemplateForm
    {
        [Required]
        public string Name { get; set; } = string.Empty;

        [Required]
        public string DisplayName { get; set; } = string.Empty;

        public string? Description { get; set; }

        public bool Global { get; set; }

        public string? CopyFrom { get; set; }

        public CreateMailTemplateRequest ToRequest() => new(Name, DisplayName, Description, Global, CopyFrom);
    }
}
