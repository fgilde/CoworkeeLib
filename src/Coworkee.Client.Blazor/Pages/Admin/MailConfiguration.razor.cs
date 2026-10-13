using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Client.Blazor.Mail;
using Coworkee.Client.Blazor.Security;
using Coworkee.Contracts.Mailing;
using Coworkee.Contracts.Settings;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using MudBlazor;

namespace Coworkee.Client.Blazor.Pages.Admin;

/// <summary>The mail provider and sender: system wide in the system organisation, otherwise the organisation's own server when it switches one on.</summary>
public partial class MailConfiguration
{
    private static readonly (string Id, string Name)[] Providers =
    [
        (MailProviders.Smtp, "SMTP"),
        (MailProviders.SendGrid, "SendGrid"),
        (MailProviders.Mailgun, "Mailgun"),
        (MailProviders.Mailchimp, "Mailchimp Transactional"),
    ];

    private MailConfigurationForm? _form;
    private MailConnectionTestDto? _test;
    private bool _system = true;
    private bool _busy;
    private bool _testing;

    [Inject] private ICoworkeeApi Api { get; set; } = null!;

    [Inject] private IMailConnectionApi Mail { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [CascadingParameter] private Task<AuthenticationState>? AuthenticationState { get; set; }

    private SettingScope Scope => _system ? SettingScope.Global : SettingScope.Tenant;

    protected override async Task OnInitializedAsync()
    {
        _system = AuthenticationState is null || !SystemTenant.IsOutside((await AuthenticationState).User);
        await LoadAsync();
    }

    private string SecretHelp(bool removed) => L[removed ? "The stored key is removed on save." : "Stored encrypted; it is never shown again."];

    private string ApiKeyHelp => _form!.DropsApiKey
        ? L["The stored key belongs to the previous provider and is removed on save; enter the key for this one."]
        : SecretHelp(_form.RemoveApiKey);

    private async Task LoadAsync()
    {
        await Snackbar.RunAsync(async () =>
        {
            var values = await Api.GetSettingsAsync(Scope);
            var defaults = _system ? (await Api.GetSettingDefinitionsAsync(false)).SelectMany(g => g.Settings).ToDictionary(d => d.Name, d => d.DefaultValue) : null;
            _form = MailConfigurationForm.Load(values, defaults, tenant: !_system);
        });
        _test = null;
    }

    private void SelectProvider(string provider)
    {
        _form!.Provider = provider;
        _test = null;
    }

    private async Task TestAsync()
    {
        _testing = true;
        await RunAsync(async () => _test = await Mail.TestAsync(_form!.ToRequest()));
        _testing = false;
    }

    private async Task SaveAsync()
    {
        _busy = true;
        if (await Snackbar.RunAsync(() => Api.SetSettingsAsync(Scope, _form!.ToSettings()), L["Saved"]))
        {
            var test = _test;
            await LoadAsync();
            _test = test;
        }

        _busy = false;
    }

    private async Task RunAsync(Func<Task> action)
    {
        _busy = true;
        await Snackbar.RunAsync(action);
        _busy = false;
    }
}
