namespace Coworkee.Contracts.Theming;

public static class ThemeSettings
{
    public const string Mode = "Theme.Mode";
    public const string ThemeId = "Theme.Id";

    /// <summary>"true" (the default): users without an uploaded picture show their Gravatar, when they have one; "false": the initials only, no request to gravatar.com.</summary>
    public const string Gravatar = "Theme.Gravatar";
}
