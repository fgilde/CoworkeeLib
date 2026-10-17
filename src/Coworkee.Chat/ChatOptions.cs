namespace Coworkee.Chat;

public sealed class ChatOptions
{
    public const string Section = "Coworkee:Chat";

    /// <summary>How long an uploaded file waits for its message; after that the cleanup deletes it.</summary>
    public TimeSpan UnsentAttachmentLifetime { get; set; } = TimeSpan.FromHours(24);
}
