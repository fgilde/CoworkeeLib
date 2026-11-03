namespace Coworkee.Contracts.Social;

/// <summary>Whether a comment waits for an answer: an Open comment is a question or request until someone closes it.</summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<CommentStatus>))]
public enum CommentStatus
{
    None = 0,
    Open = 1,
    Closed = 2,
}
