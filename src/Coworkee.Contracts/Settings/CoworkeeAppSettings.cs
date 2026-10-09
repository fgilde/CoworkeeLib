using Coworkee.Contracts.Configuration;

namespace Coworkee.Contracts.Settings;

/// <summary>
/// The typed settings admins edit under Settings, bound to the section <see cref="Section"/>. Derive from it to add sections of the app,
/// or register a type of your own with AddCoworkeeSettings on the server and in the client.
/// </summary>
public class CoworkeeAppSettings
{
    public const string Section = "Coworkee";

    public RegistrationOptions Registration { get; set; } = new();

    public NotificationOptions Notifications { get; set; } = new();

    public BackgroundJobOptions Jobs { get; set; } = new();
}
