namespace Coworkee.Contracts.Social;

/// <param name="Status">On edit: null keeps the status.</param>
/// <param name="Properties">App values (at most 20, keys up to 50 and values up to 500 characters); on edit null keeps them.</param>
public sealed record CommentRequest(string Text, Guid? ParentId = null, CommentStatus? Status = null, IReadOnlyDictionary<string, string>? Properties = null);
