namespace Coworkee.Contracts.Identity;

public static class ClientGrantTypes
{
    public const string AuthorizationCode = "authorization_code";
    public const string RefreshToken = "refresh_token";

    /// <summary>A service client: a confidential client that calls APIs on its own, without a user.</summary>
    public const string ClientCredentials = "client_credentials";

    public static readonly IReadOnlyList<string> All = [AuthorizationCode, RefreshToken, ClientCredentials];
}
