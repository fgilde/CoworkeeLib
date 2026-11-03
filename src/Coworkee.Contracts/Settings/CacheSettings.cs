namespace Coworkee.Contracts.Settings;

/// <summary>The defaults of cached queries (ICachedQuery) that name no time of their own.</summary>
public static class CacheSettings
{
    /// <summary>How long an entry lives, in minutes (5).</summary>
    public const string DurationMinutes = "Cache.DurationMinutes";

    /// <summary>Age in minutes after which an entry answers at once and is loaded again in the background; 0 for none (the default).</summary>
    public const string RefreshAfterMinutes = "Cache.RefreshAfterMinutes";

    /// <summary>Off turns background refreshing off, also for queries that name a refresh time.</summary>
    public const string RefreshInBackground = "Cache.RefreshInBackground";
}
