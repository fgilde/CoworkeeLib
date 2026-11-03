namespace Coworkee.Files;

public static class FileTokens
{
    public const string Purpose = "file";

    /// <summary>The viewer asks several times (HEAD, ranges), so the token works until it expires.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);
}
