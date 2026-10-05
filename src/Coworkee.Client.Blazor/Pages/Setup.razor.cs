using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Theming;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Theming;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Pages;

public partial class Setup
{
    [Inject] private ICoworkeeApi Api { get; set; } = null!;

    [Inject] private CoworkeeClientOptions Options { get; set; } = null!;

    [Inject] private ThemeService ThemeService { get; set; } = null!;

    private const int LastStep = 5;
    private const int LookStep = 3;
    private const int AdministratorStep = 4;
    private static readonly string[] StepTitles = ["Welcome", "System check", "Organisation", "Look and feel", "Administrator", "Mail"];
    private static readonly string[] StepHints =
    [
        "A few steps and the system is ready. Start with the setup token.",
        "Everything the system needs, checked once.",
        "The name your people will see.",
        "Pick a theme; it applies right away. You can change it any time.",
        "The first account, with full access.",
        "Optional: the server for invitations and password mails. You can set it later under Settings.",
    ];

    private readonly SetupInput _request = new();
    private readonly MailInput _mail = new();
    private IReadOnlyList<SetupCheckDto>? _checks;
    private IReadOnlyList<ThemeDto>? _themes;
    private Guid? _themeId;
    private string _confirmPassword = string.Empty;
    private bool _showPassword;
    private int _step;
    private bool _busy;
    private bool _done;
    private string? _error;
    private IReadOnlyDictionary<string, string[]>? _errors;

    private bool BlockedByChecks => _step == 1 && (_checks is null || _checks.Any(c => c.Status == SetupCheckStatus.Error));

    private InputType PasswordInput => _showPassword ? InputType.Text : InputType.Password;

    private Guid? SelectedThemeId => _themeId ?? _themes?.FirstOrDefault(t => t.Id == ThemeService.Current?.Id)?.Id ?? _themes?.FirstOrDefault(t => t.IsDefault)?.Id;

    private bool HasError(string field) => _errors?.Keys.Any(k => k.EndsWith(field, StringComparison.OrdinalIgnoreCase)) == true;

    private string? ErrorFor(string field) => _errors?.FirstOrDefault(e => e.Key.EndsWith(field, StringComparison.OrdinalIgnoreCase)).Value?.FirstOrDefault();

    private static Severity SeverityOf(SetupCheckStatus status) => status switch
    {
        SetupCheckStatus.Ok => Severity.Success,
        SetupCheckStatus.Warning => Severity.Warning,
        _ => Severity.Error,
    };

    private void Choose(Guid id)
    {
        if (_themes?.FirstOrDefault(t => t.Id == id) is { } theme)
        {
            _themeId = id;
            ThemeService.Apply(theme);
        }
    }

    private void Back()
    {
        _error = null;
        _step--;
    }

    /// <summary>Why the current step cannot be left yet, if it cannot.</summary>
    private string? Problem() => _step switch
    {
        0 when string.IsNullOrWhiteSpace(_request.SetupToken) => "Enter the setup token.",
        2 when string.IsNullOrWhiteSpace(_request.TenantName) => "Enter the name of the organisation.",
        AdministratorStep when string.IsNullOrWhiteSpace(_request.AdminEmail) || !_request.AdminEmail.Contains('@') => "Enter the administrator's email address.",
        AdministratorStep when string.IsNullOrEmpty(_request.AdminPassword) => "Choose a password.",
        AdministratorStep when _request.AdminPassword != _confirmPassword => "The passwords do not match.",
        _ => null,
    };

    private async Task NextAsync()
    {
        if (Problem() is { } problem)
        {
            _error = problem;
            return;
        }

        _error = null;
        _step++;
        if (_step == 1)
        {
            _checks = null;
            try
            {
                _checks = await Api.GetSetupChecksAsync();
            }
            catch (ApiException exception)
            {
                _checks = [new SetupCheckDto("System", SetupCheckStatus.Error, exception.Message)];
            }
        }
        else if (_step == LookStep && _themes is null)
        {
            try
            {
                _themes = await Api.GetBuiltInThemesAsync();
            }
            catch (Exception exception) when (exception is ApiException or HttpRequestException)
            {
                _themes = [];
            }
        }
    }

    private Dictionary<string, string?> Settings()
    {
        var settings = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (ThemeService.Mode is "light" or "dark")
        {
            settings["Theme.Mode"] = ThemeService.Mode;
        }

        if (string.IsNullOrWhiteSpace(_mail.Host))
        {
            return settings;
        }

        settings["Mail.Smtp.Host"] = _mail.Host.Trim();
        Add(settings, "Mail.Smtp.Port", _mail.Port);
        Add(settings, "Mail.From", _mail.From);
        Add(settings, "Mail.Smtp.UserName", _mail.UserName);
        Add(settings, "Mail.Smtp.Password", _mail.Password);
        settings["Mail.Smtp.UseSsl"] = _mail.UseSsl ? "true" : "false";
        return settings;
    }

    private static void Add(Dictionary<string, string?> settings, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            settings[name] = value.Trim();
        }
    }

    private async Task FinishAsync()
    {
        _busy = true;
        _error = null;
        _errors = null;
        try
        {
            _request.AdminEmail = _request.AdminEmail.Trim();
            await Api.CompleteSetupAsync(new CompleteSetupRequest(
                _request.SetupToken.Trim(), _request.TenantName.Trim(), _request.AdminEmail, _request.AdminPassword, _request.AdminFirstName, _request.AdminLastName,
                Settings(), _themeId));
            _done = true;
        }
        catch (ApiException exception) when (exception.Status == 403)
        {
            _error = "The setup token is not valid.";
            _step = 0;
        }
        catch (ApiException exception) when (exception.Status == 409)
        {
            _error = "The system is already set up.";
        }
        catch (ApiException exception)
        {
            _errors = exception.Errors;
            _error = exception.Errors is null ? exception.Message : "Please correct the highlighted fields: " + string.Join(" ", exception.Errors.SelectMany(e => e.Value));
            if (HasError("Password") || HasError("Email"))
            {
                _step = AdministratorStep;
            }
            else if (HasError("ThemeId"))
            {
                _step = LookStep;
            }
        }
        finally
        {
            _busy = false;
        }
    }

    private sealed class SetupInput
    {
        public string SetupToken { get; set; } = string.Empty;

        public string TenantName { get; set; } = string.Empty;

        public string AdminEmail { get; set; } = string.Empty;

        public string AdminPassword { get; set; } = string.Empty;

        public string? AdminFirstName { get; set; }

        public string? AdminLastName { get; set; }
    }

    private sealed class MailInput
    {
        public string? Host { get; set; }

        public string? Port { get; set; }

        public string? From { get; set; }

        public string? UserName { get; set; }

        public string? Password { get; set; }

        public bool UseSsl { get; set; }
    }
}
