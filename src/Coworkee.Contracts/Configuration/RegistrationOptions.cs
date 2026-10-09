using System.Globalization;

namespace Coworkee.Contracts.Configuration;

/// <summary>
/// How self registration on the auth server works. Whether it is on, needs activation or an email confirmation are settings
/// (Account.AllowRegistration, Account.RegistrationRequiresActivation, Account.RegistrationRequiresEmailConfirmation) admins change at runtime.
/// </summary>
public sealed class RegistrationOptions
{
    public const string Section = "Coworkee:Registration";

    /// <summary>Binds ASP.NET Core Identity's PasswordOptions (RequiredLength, RequireDigit, RequireUppercase, ...); applies to every password.</summary>
    public const string PasswordSection = Section + ":Password";

    public bool RequireAddress { get; set; }

    /// <summary>Patterns like "*@example.com" an address must match to register; empty allows every address.</summary>
    public List<string> AllowedEmails { get; set; } = [];

    /// <summary>Shows the document step; each slot asks for one file.</summary>
    public bool RequireDocuments { get; set; }

    public List<RegistrationDocumentSlot> Documents { get; set; } = [];
}

public sealed class RegistrationDocumentSlot
{
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>The name per language ("de": "Reisepass"); <see cref="Name"/> is the fallback and what stores file the document under.</summary>
    public Dictionary<string, string> Names { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The description per language; <see cref="Description"/> is the fallback.</summary>
    public Dictionary<string, string> Descriptions { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public bool Required { get; set; } = true;

    /// <summary>Accepted content types, wildcards allowed ("image/*"); empty accepts every type.</summary>
    [ContentTypes]
    public List<string> ContentTypes { get; set; } = [];

    /// <summary>Largest file in bytes; null or 0 for no limit.</summary>
    [FileSize]
    public long? MaxSize { get; set; }

    /// <summary>The name in the current UI language ("de-AT", then "de"), else <see cref="Name"/>.</summary>
    public string DisplayName => InLanguage(Names) ?? Name;

    public string? DisplayDescription => InLanguage(Descriptions) ?? Description;

    private static string? InLanguage(Dictionary<string, string> texts) =>
        texts.GetValueOrDefault(CultureInfo.CurrentUICulture.Name) ?? texts.GetValueOrDefault(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);
}
