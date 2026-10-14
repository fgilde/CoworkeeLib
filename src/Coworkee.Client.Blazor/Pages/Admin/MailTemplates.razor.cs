using System.ComponentModel.DataAnnotations;
using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Components.Data;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Client.Blazor.Security;
using Coworkee.Contracts.Mailing;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using MudBlazor;
using MudBlazor.Extensions.Components.ObjectEdit;
using MudBlazor.Extensions.Components.ObjectEdit.Options;

namespace Coworkee.Client.Blazor.Pages.Admin;

public partial class MailTemplates
{
    private IReadOnlyList<MailTemplateSummaryDto> _templates = [];
    private bool _system = true;
    private bool _busy;

    [Inject] private ICoworkeeApi Api { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Inject] private IDialogService Dialogs { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [Inject] private NavigationManager Navigation { get; set; } = null!;

    [CascadingParameter] private Task<AuthenticationState>? AuthenticationState { get; set; }

    protected override async Task OnInitializedAsync()
    {
        _system = AuthenticationState is null || !SystemTenant.IsOutside((await AuthenticationState).User);
        await LoadAsync();
    }

    private Task LoadAsync() => Snackbar.RunAsync(async () => _templates = await Api.GetMailTemplatesAsync());

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
            Navigation.NavigateTo($"/admin/mail/templates/{Uri.EscapeDataString(created)}/{L.Culture}");
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
