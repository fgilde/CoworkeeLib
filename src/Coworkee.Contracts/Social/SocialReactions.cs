namespace Coworkee.Contracts.Social;

public static class SocialReactions
{
    /// <summary>The entity type of reactions on comments (with <c>social.CommentReactions()</c>); the id is the comment's.</summary>
    public const string Comment = "Social.Comment";

    /// <summary>The emojis the reaction picker offers by default.</summary>
    public static readonly IReadOnlyList<string> Defaults = ["👍", "❤️", "😂", "🎉", "😮", "😢"];
}
