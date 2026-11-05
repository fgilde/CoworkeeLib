namespace Coworkee.Contracts.Settings;

public static class DialogSettings
{
    /// <summary>The dialog options the administrator changed, as JSON per part: <c>{"Small":{"maxWidth":"Large",...}}</c>; parts it does not name keep the app's options.</summary>
    public const string Options = "Dialogs.Options";
}
