namespace Coworkee.Contracts.Chat;

/// <summary>What the chat editor inserts as mention: a person (<c>@</c>) or a record found by the global search (<c>#</c>).</summary>
public sealed record ChatMentionItem(string Kind, string Id, string Label, string? Provider = null, string? Url = null, string? Icon = null)
{
    public const string User = "user";
    public const string Entity = "entity";

    public override string ToString() => Label;
}
