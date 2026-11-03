using System.ComponentModel.DataAnnotations;

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

    [Display(Name = "Ask for the address", Description = "Adds street, zip code, city and country to the registration.")]
    public bool RequireAddress { get; set; }

    /// <summary>Patterns like "*@example.com" an address must match to register; empty allows every address.</summary>
    [Display(Name = "Allowed emails", Description = "Patterns like *@example.com an address must match to register; empty allows every address.")]
    public List<string> AllowedEmails { get; set; } = [];

    /// <summary>Shows the document step; each slot asks for one file.</summary>
    [Display(Name = "Ask for documents", Description = "Adds a step where new users upload the documents listed below.")]
    public bool RequireDocuments { get; set; }

    [Display(Name = "Documents to upload", Description = "One entry per file the user uploads, e.g. passport or certificate.")]
    public List<RegistrationDocumentSlot> Documents { get; set; } = [];
}
