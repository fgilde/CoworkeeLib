namespace Coworkee.Contracts.Social;

public static class SocialPermissions
{
    public const string GroupName = "Social";

    public static class Comments
    {
        public const string View = "Social.Comments.View";
        public const string Create = "Social.Comments.Create";
        public const string Moderate = "Social.Comments.Moderate";

        /// <summary>Changes the status of any comment (open, closed, also reopening); authors give a status only when they write a comment.</summary>
        public const string SetStatus = "Social.Comments.SetStatus";
    }

    public static class Tags
    {
        public const string View = "Social.Tags.View";
        public const string Create = "Social.Tags.Create";
        public const string Moderate = "Social.Tags.Moderate";
    }

    public static class Ratings
    {
        public const string View = "Social.Ratings.View";
        public const string Create = "Social.Ratings.Create";
        public const string Moderate = "Social.Ratings.Moderate";
    }

    public static class Reactions
    {
        public const string View = "Social.Reactions.View";
        public const string Create = "Social.Reactions.Create";
    }
}
