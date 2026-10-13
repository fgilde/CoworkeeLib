namespace Coworkee.Contracts.Configuration;

/// <summary>Names of the password rule settings, shared by the server and the clients that show the rules.</summary>
public static class PasswordSettings
{
    public const string RequiredLength = "Account.Password.RequiredLength";

    public const string RequiredUniqueChars = "Account.Password.RequiredUniqueChars";

    public const string RequireDigit = "Account.Password.RequireDigit";

    public const string RequireLowercase = "Account.Password.RequireLowercase";

    public const string RequireUppercase = "Account.Password.RequireUppercase";

    public const string RequireNonAlphanumeric = "Account.Password.RequireNonAlphanumeric";
}
