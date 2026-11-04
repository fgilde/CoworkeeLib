namespace Coworkee.Contracts.Ai;

/// <summary>Where users find the assistant (setting <see cref="AiSettingNames.Display"/>); Ctrl+I opens it in every mode while it is available.</summary>
public static class AssistantDisplay
{
    public const string Off = "Off";

    public const string BottomRight = "BottomRight";

    public const string BottomLeft = "BottomLeft";

    public const string AppBar = "AppBar";

    public const string Navigation = "Navigation";

    public static readonly string[] All = [Off, BottomRight, BottomLeft, AppBar, Navigation];
}
