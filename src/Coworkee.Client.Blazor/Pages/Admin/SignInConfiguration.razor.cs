using System.Text.Json;
using System.Text.Json.Nodes;
using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Components;
using Coworkee.Client.Blazor.Components.Data;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Client.Blazor.Security;
using Coworkee.Contracts.Configuration;
using Coworkee.Contracts.Files;
using Coworkee.Contracts.Realtime;
using Coworkee.Contracts.Settings;
using Coworkee.Contracts.Theming;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using MudBlazor;
using MudBlazor.Extensions;

namespace Coworkee.Client.Blazor.Pages.Admin;

/// <summary>
/// The sign-in and registration configuration in one place, arranged by topic (methods, security, registration, appearance, texts) with one save bar. Nothing is stored
/// differently than under Settings: the setting groups Security and Account stay settings, the registration stays the part Registration of the app settings and the
/// design stays the section <see cref="SignInDesign.Section"/>; saving sends only what changed, in the same form the generic pages do.
/// </summary>
public partial class SignInConfiguration
{
    // settings of the Account module, which the client does not reference
    private const string PasswordExpiryDays = "Account.Password.ExpiryDays";
    private const string PasswordHistory = "Account.Password.History";
    private const string MaxFailedAttempts = "Account.Lockout.MaxFailedAttempts";
    private const string LockoutMinutes = "Account.Lockout.Minutes";
    private const string AllowRegistration = "Account.AllowRegistration";
    private const string RegistrationRequiresActivation = "Account.RegistrationRequiresActivation";
    private const string RegistrationRequiresEmailConfirmation = "Account.RegistrationRequiresEmailConfirmation";

    private static readonly IReadOnlySet<string> SettingGroups = new HashSet<string>(StringComparer.Ordinal) { "Security", "Account" };

    private static readonly string[] PasswordRules =
    [
        PasswordSettings.RequiredLength, PasswordSettings.RequiredUniqueChars, PasswordExpiryDays, PasswordHistory,
        PasswordSettings.RequireDigit, PasswordSettings.RequireLowercase, PasswordSettings.RequireUppercase, PasswordSettings.RequireNonAlphanumeric,
    ];

    private static readonly string[] LockoutSettings = [MaxFailedAttempts, LockoutMinutes];

    private static readonly string[] SessionSettings = [AuthSettings.MaxSessionsPerUser, AuthSettings.SessionLimitMode];

    private static readonly string[] RegistrationSettings = [AllowRegistration, RegistrationRequiresActivation, RegistrationRequiresEmailConfirmation];

    // what the page arranges itself; any other setting of the two groups (an app's own, the active role) shows under More settings
    private static readonly HashSet<string> Arranged =
        [AuthSettings.LoginMode, AuthSettings.PasskeysEnabled, .. PasswordRules, .. LockoutSettings, .. SessionSettings, .. RegistrationSettings];

    private static readonly (string Value, string Label, string Icon)[] LoginModes =
    [
        (nameof(Contracts.Configuration.LoginMode.Internal), "Password", Icons.Material.Outlined.Password),
        (nameof(Contracts.Configuration.LoginMode.External), "External provider", Icons.Material.Outlined.Key),
        (nameof(Contracts.Configuration.LoginMode.Both), "Both", Icons.Material.Outlined.JoinInner),
    ];

    private IReadOnlyDictionary<string, string> SessionModes => new Dictionary<string, string>
    {
        [SessionLimitModes.EndOldest] = L["End the oldest sessions"],
        [SessionLimitModes.Refuse] = L["Refuse new sign-ins"],
    };

    private const string EmailPattern = "*@example.com";

    private static readonly char[] EmailDelimiters = [' ', ',', ';'];

    private const string RegistrationPath = nameof(CoworkeeAppSettings.Registration);

    private bool _system = true;
    private bool _saving;
    private bool _documentsAvailable;
    private int _tab;
    private string _language = string.Empty;

    private IReadOnlyList<SettingGroupDto> _groups = [];
    private IReadOnlyDictionary<string, SettingValueDto> _stored = new Dictionary<string, SettingValueDto>();
    private SettingsModel? _settings;

    private JsonObject? _app;
    private string _registrationKey = "registration";
    private RegistrationOptions? _registration;
    private string? _registrationSaved;
    private IReadOnlyList<string> _locked = [];
    private IReadOnlyList<string> _hidden = [];

    private SignInDesign? _design;
    private string? _designSaved;

    private enum TextPart
    {
        Title,
        Subtitle,
        Notice,
    }

    [Inject] private ICoworkeeApi Api { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [Inject] private IDialogService Dialogs { get; set; } = null!;

    [Inject] private IEnumerable<ClientAppConfiguration> Registrations { get; set; } = null!;

    [Inject] private CoworkeeClientOptions Options { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Inject] private Theming.ThemeService ThemeService { get; set; } = null!;

    [Inject] private PermissionStore Permissions { get; set; } = null!;

    [CascadingParameter] private Task<AuthenticationState>? AuthenticationState { get; set; }

    // other organisations change their own session limits; everything else is system wide
    private SettingScope Scope => _system ? SettingScope.Global : SettingScope.Tenant;

    // the app settings carry the registration when they derive from CoworkeeAppSettings
    private ClientAppConfiguration? RegistrationSection =>
        Registrations.FirstOrDefault(r => r.IsAppSettings && typeof(CoworkeeAppSettings).IsAssignableFrom(r.Type));

    private bool RegistrationDirty => _registration is not null && Json(_registration) != _registrationSaved;

    private bool DesignDirty => _design is not null && Json(_design) != _designSaved;

    private bool Dirty => _settings?.Changes().Count > 0 || RegistrationDirty || DesignDirty;

    private bool HasMethods => _system || Any([AuthSettings.LoginMode, AuthSettings.PasskeysEnabled]);

    private bool HasSecurity => Any(PasswordRules) || Any(LockoutSettings) || Any(SessionSettings) || Others.Count > 0;

    private bool HasRegistration => Any(RegistrationSettings) || _registration is not null;

    private IReadOnlyList<SettingDefinitionDto> Others => _settings is null ? [] : [.. _settings.Definitions.Where(d => !Arranged.Contains(d.Name))];

    private string? LoginMode => _settings?.Definition(AuthSettings.LoginMode) is { } mode ? mode.FixedValue ?? _settings.Get(mode.Name) as string : null;

    // a registration store needs the Files module (or the app's own): its permissions tell whether it is there
    private bool DocumentsEditable => _documentsAvailable && !Locked(nameof(RegistrationOptions.Documents));

    protected override async Task OnInitializedAsync()
    {
        _system = AuthenticationState is null || !SystemTenant.IsOutside((await AuthenticationState).User);
        await LoadSettingsAsync();
        await LoadRegistrationAsync();
        await LoadDesignAsync();
        _documentsAvailable = _registration is not null && await FilesAvailableAsync();
    }

    private bool Any(IEnumerable<string> names) => _settings is not null && names.Any(n => _settings.Definition(n) is not null);

    private void Changed()
    {
        // re-renders the page: the save bar follows what changed
    }

    private async Task LoadSettingsAsync()
    {
        var definitions = await Api.GetSettingDefinitionsAsync(false);
        _stored = (await Api.GetSettingsAsync(Scope)).ToDictionary(v => v.Name, StringComparer.Ordinal);
        _groups = SettingsModel.Select(definitions, Scope, SettingGroups);
        _settings = new SettingsModel(_groups, _stored, Scope);
    }

    private async Task LoadRegistrationAsync()
    {
        if (!_system || RegistrationSection is not { } section)
        {
            return;
        }

        try
        {
            ApplyRegistration(await Api.GetAppConfigurationAsync(section.Section));
        }
        catch (ApiException)
        {
            _registration = null;
        }
    }

    private async Task LoadDesignAsync()
    {
        if (!_system || !Options.AllowSignInDesign)
        {
            return;
        }

        try
        {
            ApplyDesign(await Api.GetAppConfigurationAsync(SignInDesign.Section));
        }
        catch (ApiException)
        {
            // outside the system organisation or while the API refuses changes the appearance stays away
            _design = null;
        }
    }

    // the whole app settings stay as they came; only their part Registration is edited and put back
    private void ApplyRegistration(AppConfigurationValuesDto section)
    {
        _app = JsonNode.Parse(section.Values.GetRawText()) as JsonObject ?? [];
        _registrationKey = _app.Select(p => p.Key).FirstOrDefault(k => string.Equals(k, RegistrationPath, StringComparison.OrdinalIgnoreCase)) ?? "registration";
        _registration = _app[_registrationKey]?.Deserialize<RegistrationOptions>(JsonSerializerOptions.Web) ?? new RegistrationOptions();
        _registrationSaved = Json(_registration);
        _locked = [.. section.Locked ?? [], .. section.Protected ?? []];
        _hidden = section.Hidden ?? [];
    }

    private void ApplyDesign(AppConfigurationValuesDto section)
    {
        _design = section.Values.Deserialize<SignInDesign>(JsonSerializerOptions.Web) ?? new SignInDesign();
        _designSaved = Json(_design);
    }

    private async Task<bool> FilesAvailableAsync()
    {
        if (await Permissions.HasAsync(FilePermissions.View))
        {
            return true;
        }

        try
        {
            return (await Api.GetPermissionDefinitionsAsync()).SelectMany(g => g.Permissions).Any(p => p.Name == FilePermissions.View);
        }
        catch (ApiException)
        {
            return false;
        }
    }

    private static string Json<T>(T value) => JsonSerializer.Serialize(value, JsonSerializerOptions.Web);

    // the server names paths below the section like configuration keys ("Registration:AllowedEmails"); a locked or hidden parent covers its children
    private static bool Covers(IReadOnlyList<string> paths, string property)
    {
        var path = $"{RegistrationPath}:{property}";
        return paths.Any(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(p + ":", StringComparison.OrdinalIgnoreCase) || p.StartsWith(path + ":", StringComparison.OrdinalIgnoreCase));
    }

    private bool Locked(string property) => Covers(_locked, property);

    private bool Shows(string property) => !Covers(_hidden, property);

    private string Hint(string property, string text) => Locked(property) ? $"{text} {L["Set in the configuration"]}" : text;

    private void SetLoginMode(string? mode)
    {
        if (mode is not null)
        {
            _settings!.Set(AuthSettings.LoginMode, mode);
        }
    }

    private void SetAllowedEmails(List<string>? emails) => _registration!.AllowedEmails = [.. emails ?? []];

    private void AddSlot() => _registration!.Documents.Add(new RegistrationDocumentSlot());

    private void RemoveSlot(RegistrationDocumentSlot slot) => _registration!.Documents.Remove(slot);

    private static void SetTypes(RegistrationDocumentSlot slot, IReadOnlyList<string> types) => slot.ContentTypes = [.. types];

    private static void SetSize(RegistrationDocumentSlot slot, long? size) => slot.MaxSize = size;

    private static void SetNames(RegistrationDocumentSlot slot, IReadOnlyDictionary<string, string> names) => slot.Names = new(names, StringComparer.OrdinalIgnoreCase);

    private static void SetHints(RegistrationDocumentSlot slot, IReadOnlyDictionary<string, string> hints) => slot.Descriptions = new(hints, StringComparer.OrdinalIgnoreCase);

    private void SelectLanguage(string? culture) => _language = culture ?? string.Empty;

    private Dictionary<string, string> Translations(TextPart part) => part switch
    {
        TextPart.Title => _design!.Texts.Titles,
        TextPart.Subtitle => _design!.Texts.Subtitles,
        _ => _design!.Texts.Notices,
    };

    private string? Default(TextPart part) => part switch
    {
        TextPart.Title => _design!.Texts.Title,
        TextPart.Subtitle => _design!.Texts.Subtitle,
        _ => _design!.Texts.Notice,
    };

    private string? Text(TextPart part) => _language.Length == 0 ? Default(part) : Translations(part).GetValueOrDefault(_language);

    // a language without its own text shows the one for every language
    private string? Fallback(TextPart part) => _language.Length == 0 ? null : Default(part);

    private void SetText(TextPart part, string? text)
    {
        var value = string.IsNullOrWhiteSpace(text) ? null : text;
        if (_language.Length > 0)
        {
            if (value is null)
            {
                Translations(part).Remove(_language);
            }
            else
            {
                Translations(part)[_language] = value;
            }

            return;
        }

        var texts = _design!.Texts;
        switch (part)
        {
            case TextPart.Title:
                texts.Title = value;
                break;
            case TextPart.Subtitle:
                texts.Subtitle = value;
                break;
            default:
                texts.Notice = value;
                break;
        }
    }

    /// <summary>Saves what changed: the settings in one call, the app settings with their part Registration, the design; each part keeps its own errors.</summary>
    public async Task SaveAsync()
    {
        _saving = true;
        try
        {
            var saved = true;
            if (_settings?.Changes() is { Count: > 0 } changes)
            {
                saved &= await Snackbar.RunAsync(async () =>
                {
                    await Api.SetSettingsAsync(Scope, changes);
                    await ThemeService.LoadAsync();
                    await LoadSettingsAsync();
                });
            }

            if (RegistrationDirty && RegistrationSection is { } section)
            {
                saved &= await Snackbar.RunAsync(async () =>
                {
                    var values = (JsonObject)_app!.DeepClone();
                    values[_registrationKey] = JsonSerializer.SerializeToNode(_registration, JsonSerializerOptions.Web);
                    ApplyRegistration(await Api.SaveAppConfigurationAsync(section.Section, JsonSerializer.SerializeToElement(values, JsonSerializerOptions.Web)));
                });
            }

            if (DesignDirty)
            {
                saved &= await Snackbar.RunAsync(async () =>
                    ApplyDesign(await Api.SaveAppConfigurationAsync(SignInDesign.Section, JsonSerializer.SerializeToElement(_design, JsonSerializerOptions.Web))));
            }

            if (saved)
            {
                Snackbar.Add(L["Settings saved."], Severity.Success);
            }
        }
        finally
        {
            _saving = false;
        }
    }

    private void Discard()
    {
        _settings = new SettingsModel(_groups, _stored, Scope);
        if (_registrationSaved is not null)
        {
            _registration = JsonSerializer.Deserialize<RegistrationOptions>(_registrationSaved, JsonSerializerOptions.Web);
        }

        if (_designSaved is not null)
        {
            _design = JsonSerializer.Deserialize<SignInDesign>(_designSaved, JsonSerializerOptions.Web);
        }
    }

    private async Task ResetDesignAsync()
    {
        if (await Dialogs.ConfirmAsync(L["Restore defaults"], L["All changes made here go away; the configured values apply again."], L["Restore"], L["Cancel"],
                Icons.Material.Outlined.SettingsBackupRestore))
        {
            await Snackbar.RunAsync(async () => ApplyDesign(await Api.ResetAppConfigurationAsync(SignInDesign.Section)));
        }
    }

    // someone saved a setting of this scope: show what applies now, unless the page holds changes of its own
    private async Task SettingsChangedAsync()
    {
        if (!_saving && _settings?.Changes() is not { Count: > 0 })
        {
            await LoadSettingsAsync();
        }
    }

    // another administrator saved the app settings or the design: reload the part that has no changes here
    private async Task ConfigurationChangedAsync(RealtimeEnvelope envelope)
    {
        if (_saving)
        {
            return;
        }

        if (_registration is not null && !RegistrationDirty && RegistrationSection is { } section && Components.AppConfigurationChanges.Touches(envelope, section.Section, Registrations))
        {
            await LoadRegistrationAsync();
        }

        if (_design is not null && !DesignDirty && Components.AppConfigurationChanges.Touches(envelope, SignInDesign.Section, Registrations))
        {
            await LoadDesignAsync();
        }
    }
}
