using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Components.Mail;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.Localization;
using Coworkee.Contracts.Mailing;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Pages.Admin;

public partial class MailTemplateDetail : IDisposable
{
    private bool _canSend;
    private List<BreadcrumbItem> _breadcrumbs = [];
    private MailTemplateSummaryDto? _summary;
    private IReadOnlyList<LanguageDto> _languages = [];

    [Inject] private Navigation.CoworkeePages Links { get; set; } = null!;

    [Inject] private NavigationManager Navigation { get; set; } = null!;

    [Inject] private ICoworkeeApi Api { get; set; } = null!;

    [Inject] private ILocalizationApi Localization { get; set; } = null!;

    [Inject] private IDialogService Dialogs { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Inject] private Security.PermissionStore Permissions { get; set; } = null!;

    [Parameter] public string Name { get; set; } = string.Empty;

    [Parameter] public string Culture { get; set; } = string.Empty;

    private MailTemplateLanguages Variants => new(Api, Dialogs, Snackbar, L, _languages);

    protected override async Task OnInitializedAsync()
    {
        Permissions.Changed += OnPermissionsChanged;
        await CheckSendAsync();
    }

    protected override async Task OnParametersSetAsync()
    {
        _breadcrumbs = [new BreadcrumbItem(L["Mail templates"], Links.Href("/admin/mail/templates")), new BreadcrumbItem(Name, null, disabled: true)];
        await Snackbar.RunAsync(async () =>
        {
            _summary = (await Api.GetMailTemplatesAsync()).FirstOrDefault(t => t.Name == Name);
            _languages = await Localization.GetLanguagesAsync();
        });
    }

    // the editor's guard asks first when the current language has unsaved changes
    private void Open(string culture) => Navigation.NavigateTo(Links.Href($"/admin/mail/templates/{Uri.EscapeDataString(Name)}/{culture}")!);

    private async Task AddLanguageAsync()
    {
        if (_summary is not null && await Variants.AddAsync(_summary) is { } culture)
        {
            Open(culture);
        }
    }

    // sending for real takes the campaign right and the right to choose users; follows permission changes live
    private async Task CheckSendAsync() =>
        _canSend = await Permissions.HasAsync([MailPermissions.Campaigns.Send, Contracts.Identity.IdentityPermissions.Users.View], Security.PermissionMatch.All);

    private void OnPermissionsChanged() => _ = InvokeAsync(async () =>
    {
        await CheckSendAsync();
        StateHasChanged();
    });

    private async Task SendAsync()
    {
        MailTemplateDto? template = null;
        if (await Snackbar.RunAsync(async () => template = await Api.GetMailTemplateAsync(Name, Culture)) && template is not null)
        {
            await MailTemplateSendDialog.ShowAsync(Dialogs, L, template);
        }
    }

    public void Dispose() => Permissions.Changed -= OnPermissionsChanged;
}
