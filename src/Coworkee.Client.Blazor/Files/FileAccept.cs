namespace Coworkee.Client.Blazor.Files;

/// <summary>Matches a file against an HTML accept list such as "image/*,.pdf".</summary>
public static class FileAccept
{
    public static bool Matches(string? accept, string name, string contentType) =>
        string.IsNullOrWhiteSpace(accept) || accept.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Any(pattern => pattern switch
        {
            ['.', ..] => name.EndsWith(pattern, StringComparison.OrdinalIgnoreCase),
            [.., '/', '*'] => contentType.StartsWith(pattern[..^1], StringComparison.OrdinalIgnoreCase),
            _ => string.Equals(contentType, pattern, StringComparison.OrdinalIgnoreCase),
        });
}
