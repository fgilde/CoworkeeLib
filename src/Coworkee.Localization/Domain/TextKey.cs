using Coworkee.Domain;

namespace Coworkee.Localization.Domain;

/// <summary>A key some client asked for; lists texts nobody put into a resource yet.</summary>
[NotAudited]
public sealed class TextKey : Entity
{
    public required string Key { get; set; }

    public DateTimeOffset FirstSeenAt { get; set; }
}
