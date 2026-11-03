namespace Coworkee.Mailing;

public static class MailWhitelist
{
    public static bool Allows(string? whitelist, string address)
    {
        var entries = (whitelist ?? string.Empty).Split(['\n', '\r', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return entries.Length == 0 || entries.Any(entry => entry.StartsWith('@')
            ? address.EndsWith(entry, StringComparison.OrdinalIgnoreCase)
            : string.Equals(entry, address, StringComparison.OrdinalIgnoreCase));
    }
}
