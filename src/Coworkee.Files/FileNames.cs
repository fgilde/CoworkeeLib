namespace Coworkee.Files;

public static class FileNames
{
    public const int MaxLength = 255;

    /// <summary>The trimmed name, or null when it is empty, too long or contains path separators or control characters.</summary>
    public static string? Clean(string? name)
    {
        var trimmed = name?.Trim();
        return string.IsNullOrEmpty(trimmed) || trimmed.Length > MaxLength || trimmed is "." or ".." || trimmed.Any(c => c is '/' or '\\' || char.IsControl(c))
            ? null
            : trimmed;
    }
}
