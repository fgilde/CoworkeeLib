namespace Coworkee.Contracts.Chat;

public static class ChatEvents
{
    /// <summary>
    /// Sent on the topic "user:{id}" of every member with a <see cref="ChatMessageDto"/> when a message arrives, changes, is deleted or gets a reaction.
    /// Mentions of records are not resolved in it (<see cref="ChatMessageDto.HasEntityMentions"/>): load the message to see them with the reader's rights.
    /// </summary>
    public const string Message = "chat.message";

    /// <summary>Sent on the topic "user:{id}" of every member with a <see cref="ChatReadDto"/>: unread counts and read marks follow in every tab.</summary>
    public const string Read = "chat.read";

    /// <summary>Sent on the topic "user:{id}" of the other members with a <see cref="ChatTypingDto"/> while someone writes.</summary>
    public const string Typing = "chat.typing";

    /// <summary>Sent on the topic "user:{id}" of every member, old and new, with a <see cref="ChatConversationDto"/> when name or members change.</summary>
    public const string Conversation = "chat.conversation";
}
