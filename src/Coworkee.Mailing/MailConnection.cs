namespace Coworkee.Mailing;

public sealed record MailConnection(string Provider, IReadOnlyDictionary<string, string?> Values)
{
    public string? this[string name] => Values.GetValueOrDefault(name);
}
