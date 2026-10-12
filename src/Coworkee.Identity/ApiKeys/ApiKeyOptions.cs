namespace Coworkee.Identity.ApiKeys;

/// <summary>Section Coworkee:ApiKeys.</summary>
public sealed class ApiKeyOptions
{
    public const string Section = "Coworkee:ApiKeys";

    /// <summary>Keys a user may have that are neither revoked nor expired.</summary>
    public int MaxActivePerUser { get; set; } = 20;
}
