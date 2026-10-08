namespace Coworkee.Contracts.Configuration;

public sealed class AccountOptions
{
    public const string Section = "Coworkee:Account";

    public string PublicAuthUrl { get; set; } = string.Empty;
}
