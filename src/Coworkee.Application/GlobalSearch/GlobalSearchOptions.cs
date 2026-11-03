namespace Coworkee.Application.GlobalSearch;

public sealed class GlobalSearchOptions
{
    public const string Section = "Coworkee:GlobalSearch";

    /// <summary>A provider still searching after this time ends as timed out; the others are not held up.</summary>
    public TimeSpan ProviderTimeout { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>Hits per provider when the caller asks for no limit.</summary>
    public int DefaultLimit { get; set; } = 5;

    public int MaxLimit { get; set; } = 50;
}
