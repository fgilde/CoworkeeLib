namespace Coworkee.Social;

internal static class CommentPropertyRules
{
    public const string Message = "At most 20 values, names up to 50 and values up to 500 characters.";

    public static bool Valid(IReadOnlyDictionary<string, string>? properties) =>
        properties is null || (properties.Count <= SocialLimits.CommentProperties
            && properties.All(p => p.Key.Length is > 0 and <= SocialLimits.PropertyName && (p.Value?.Length ?? 0) <= SocialLimits.PropertyValue));
}
