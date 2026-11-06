namespace Coworkee.Contracts.Settings;

public static class HeaderSettings
{
    /// <summary>The app bar as JSON, in order: <c>[{"id":"search","placement":"Bar"},{"id":"rtl","placement":"Hidden"}]</c>; placement Bar, Overflow, SecondBar, SecondOverflow or Hidden.</summary>
    public const string Items = "Header.Items";

    /// <summary>Where the app bar sits: Top (default), Bottom, Left or Right.</summary>
    public const string Position = "Header.Position";

    /// <summary>Where a second app bar sits (Top, Bottom, Left or Right); empty: there is none.</summary>
    public const string SecondPosition = "Header.SecondPosition";
}
