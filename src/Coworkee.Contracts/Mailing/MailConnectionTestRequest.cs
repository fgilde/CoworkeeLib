namespace Coworkee.Contracts.Mailing;

/// <summary>The mail settings as typed in the form; an empty secret stands for the stored one unless its setting name is in <paramref name="Cleared"/> (the form removes it).</summary>
public sealed record MailConnectionTestRequest(IReadOnlyDictionary<string, string?> Values, IReadOnlyCollection<string>? Cleared = null);
