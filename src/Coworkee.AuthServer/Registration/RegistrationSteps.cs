using Coworkee.Contracts.Configuration;

namespace Coworkee.AuthServer.Registration;

public static class RegistrationSteps
{
    public const string Account = "account";
    public const string Profile = "profile";
    public const string Roles = "roles";
    public const string Documents = "documents";
    public const string Summary = "summary";

    /// <summary>
    /// The steps in order; the last one shows the summary (and takes the documents, files cannot wait for a later step). An external
    /// sign-up has its account from the provider and starts with the personal data.
    /// </summary>
    public static IReadOnlyList<string> For(RegistrationOptions options, bool hasRoles, bool external = false) =>
        [.. external ? Array.Empty<string>() : [Account], Profile, .. hasRoles ? [Roles] : Array.Empty<string>(), options.RequireDocuments && options.Documents.Count > 0 ? Documents : Summary];

    public static string Title(string step) => step switch
    {
        Account => "Account",
        Profile => "Personal data",
        Roles => "Role",
        Documents => "Documents",
        _ => "Summary",
    };
}
