using System.Globalization;
using Coworkee.Contracts.Mailing;
using Coworkee.Contracts.Settings;

namespace Coworkee.Client.Blazor.Mail;

/// <summary>
/// The mail settings of one scope as the mail page edits them. Stored secrets never reach the browser: they stay until a new one is
/// typed or removal is chosen. An organisation without its own server saves only that switch and keeps its other values.
/// </summary>
public sealed class MailConfigurationForm
{
    private string _savedProvider = MailProviders.Smtp;
    private bool _hasApiKey;
    private bool _hasPassword;

    public bool Tenant { get; init; }

    public bool OwnServer { get; set; }

    public string Provider { get; set; } = MailProviders.Smtp;

    public string? Host { get; set; }

    public int? Port { get; set; } = 25;

    public bool UseSsl { get; set; }

    public string? UserName { get; set; }

    public string? NewPassword { get; set; }

    public bool RemovePassword { get; set; }

    public string? NewApiKey { get; set; }

    public bool RemoveApiKey { get; set; }

    public string? MailgunDomain { get; set; }

    public string MailgunRegion { get; set; } = "us";

    public string? Sender { get; set; }

    public string? Whitelist { get; set; }

    public bool KeepsPassword => _hasPassword && !RemovePassword && string.IsNullOrWhiteSpace(NewPassword);

    /// <summary>The stored key applies: nothing typed, not removed, same provider (a key is only valid for the provider it was saved for).</summary>
    public bool KeepsApiKey => _hasApiKey && !RemoveApiKey && string.IsNullOrWhiteSpace(NewApiKey) && Provider == _savedProvider;

    /// <summary>A stored key that will be dropped on save because the provider changed.</summary>
    public bool DropsApiKey => _hasApiKey && !RemoveApiKey && string.IsNullOrWhiteSpace(NewApiKey) && Provider != _savedProvider;

    public bool UsesApiKey => Provider != MailProviders.Smtp;

    public bool CanTest
    {
        get
        {
            var hasKey = !string.IsNullOrWhiteSpace(NewApiKey) || KeepsApiKey;
            return Provider switch
            {
                MailProviders.Smtp => !string.IsNullOrWhiteSpace(Host),
                MailProviders.Mailgun => hasKey && !string.IsNullOrWhiteSpace(MailgunDomain),
                _ => hasKey,
            };
        }
    }

    /// <summary>The stored values over the <paramref name="defaults"/> of the configuration (system scope only: an organisation enters its own server explicitly).</summary>
    public static MailConfigurationForm Load(IReadOnlyList<SettingValueDto> values, IReadOnlyDictionary<string, string?>? defaults, bool tenant)
    {
        string? Get(string name) => values.FirstOrDefault(v => v.Name == name) is { HasValue: true } stored
            ? stored.Value is { Length: > 0 } value ? value : null
            : defaults?.GetValueOrDefault(name) is { Length: > 0 } configured ? configured : null;
        var provider = Get(MailSettings.Provider) is { } stored && MailProviders.All.Contains(stored) ? stored : MailProviders.Smtp;
        return new MailConfigurationForm
        {
            Tenant = tenant,
            OwnServer = bool.TryParse(Get(MailSettings.UseOwnServer), out var own) && own,
            Provider = provider,
            _savedProvider = provider,
            Host = Get(MailSettings.Host),
            Port = int.TryParse(Get(MailSettings.Port), CultureInfo.InvariantCulture, out var port) ? port : 25,
            UseSsl = bool.TryParse(Get(MailSettings.UseSsl), out var ssl) && ssl,
            UserName = Get(MailSettings.UserName),
            MailgunDomain = Get(MailSettings.MailgunDomain),
            MailgunRegion = Get(MailSettings.MailgunRegion) is "eu" ? "eu" : "us",
            Sender = Get(MailSettings.From),
            Whitelist = Get(MailSettings.Whitelist),
            _hasApiKey = values.Any(v => v.Name == MailSettings.ApiKey && v.HasValue),
            _hasPassword = values.Any(v => v.Name == MailSettings.Password && v.HasValue),
        };
    }

    /// <summary>The mail keys of this scope; null values fall back to the defaults.</summary>
    public IReadOnlyDictionary<string, string?> ToSettings()
    {
        if (Tenant && !OwnServer)
        {
            return new Dictionary<string, string?> { [MailSettings.UseOwnServer] = "false" };
        }

        var values = Values();
        if (Tenant)
        {
            values[MailSettings.UseOwnServer] = "true";
        }

        values[MailSettings.Whitelist] = Trim(Whitelist);
        AddSecret(values, MailSettings.Password, NewPassword, _hasPassword, RemovePassword);
        AddSecret(values, MailSettings.ApiKey, NewApiKey, _hasApiKey, RemoveApiKey || DropsApiKey);
        return values;
    }

    /// <summary>The draft for the test mail: a missing secret lets the server take the stored one, unless it is removed or dropped.</summary>
    public MailConnectionTestRequest ToRequest()
    {
        var values = Values();
        values[MailSettings.Password] = Trim(NewPassword);
        values[MailSettings.ApiKey] = Trim(NewApiKey);
        List<string> cleared = [];
        if (_hasPassword && !KeepsPassword)
        {
            cleared.Add(MailSettings.Password);
        }

        if (_hasApiKey && !KeepsApiKey)
        {
            cleared.Add(MailSettings.ApiKey);
        }

        return new MailConnectionTestRequest(values, cleared);
    }

    private Dictionary<string, string?> Values() => new()
    {
        [MailSettings.Provider] = Provider,
        [MailSettings.Host] = Trim(Host),
        [MailSettings.Port] = Port?.ToString(CultureInfo.InvariantCulture),
        [MailSettings.UseSsl] = UseSsl ? "true" : "false",
        [MailSettings.UserName] = Trim(UserName),
        [MailSettings.MailgunDomain] = Trim(MailgunDomain),
        [MailSettings.MailgunRegion] = MailgunRegion,
        [MailSettings.From] = Trim(Sender),
    };

    private static void AddSecret(Dictionary<string, string?> values, string name, string? typed, bool stored, bool remove)
    {
        if (!string.IsNullOrWhiteSpace(typed))
        {
            values[name] = typed.Trim();
        }
        else if (stored && remove)
        {
            values[name] = null;
        }
    }

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
