namespace Coworkee.Contracts.Identity;

/// <summary>How API keys travel: <c>Authorization: Bearer cwk_…</c> or the <see cref="Header"/> header.</summary>
public static class ApiKeyDefaults
{
    public const string Scheme = "ApiKey";

    public const string Prefix = "cwk_";

    public const string Header = "X-Api-Key";

    public const string IdClaim = "coworkee:api_key";

    public const string ScopeClaim = "coworkee:scope";

    public const int MaxLifetimeDays = 366;
}
