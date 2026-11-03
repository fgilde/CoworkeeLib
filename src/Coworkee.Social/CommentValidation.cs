using Coworkee.Contracts.Social;

namespace Coworkee.Social;

/// <summary>What is about to happen to a comment; <see cref="CommentId"/> is null while it is added.</summary>
public sealed record CommentValidation(
    CommentAction Action, string EntityType, Guid EntityId, string Text, CommentStatus Status, IReadOnlyDictionary<string, string>? Properties, Guid? CommentId, Guid? ParentId);
