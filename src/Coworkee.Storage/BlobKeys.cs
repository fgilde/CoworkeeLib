namespace Coworkee.Storage;

public static class BlobKeys
{
    /// <summary>A fresh key <c>{tenant}/{yyyy}/{MM}/{guid}</c>.</summary>
    public static string New(Guid tenantId, TimeProvider clock)
    {
        var now = clock.GetUtcNow();
        return $"{tenantId:N}/{now:yyyy}/{now:MM}/{Guid.CreateVersion7():N}";
    }

    /// <summary>Segments of lower ASCII letters, digits, '-', '_' and '.', separated by single '/', never '.' or '..'.</summary>
    public static string Validate(string key)
    {
        var segments = key.Split('/');
        if (key.Length is 0 or > 512 || segments.Any(s => s.Length == 0 || s is "." or ".." || !s.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.')))
        {
            throw new ArgumentException($"'{key}' is not a valid blob key.", nameof(key));
        }

        return key;
    }
}
