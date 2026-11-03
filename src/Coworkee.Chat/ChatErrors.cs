using Coworkee.Core.Results;

namespace Coworkee.Chat;

internal static class ChatErrors
{
    public static readonly Error UnknownPerson = Error.NotFound("chat.unknown_person", "There is no such person in the organisation.");
    public static readonly Error NotFound = Error.NotFound("chat.not_found", "There is no such chat.");
    public static readonly Error MessageNotFound = Error.NotFound("chat.message_not_found", "There is no such message.");
    public static readonly Error NotYours = Error.Forbidden("chat.not_yours", "Only the sender changes or deletes a message.");
    public static readonly Error NotOwner = Error.Forbidden("chat.not_owner", "Only the owner of the group removes others.");
    public static readonly Error NotAGroup = Error.Validation("chat.not_a_group", "A direct chat has no name and keeps its two people.");
    public static readonly Error Empty = Error.Validation("chat.empty", "Write something or attach a file.");
    public static readonly Error TooLarge = Error.Validation("chat.too_large", "The file is larger than allowed.");
    public static readonly Error Rejected = Error.Validation("chat.rejected", "The file was rejected by the virus scan.");
    public static readonly Error InvalidName = Error.Validation("chat.invalid_name", "The file name is not valid.");
}
