namespace Coworkee.Contracts.Configuration;

/// <summary>The database engine of the app; the Aspire app host sets it, the migrations of the app must fit it.</summary>
public sealed class DatabaseOptions
{
    public const string Section = "Coworkee:Database";

    public const string ProviderKey = Section + ":Provider";

    /// <summary>One of <see cref="DatabaseProviders"/>.</summary>
    public string Provider { get; set; } = DatabaseProviders.Postgres;
}
