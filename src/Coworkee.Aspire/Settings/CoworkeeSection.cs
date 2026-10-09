using Coworkee.Contracts.Configuration;

namespace Coworkee.Aspire.Settings;

public sealed class CoworkeeSection
{
    public AuthServerOptions Auth { get; } = new();

    public BffOptions Bff { get; } = new();

    public AccountOptions Account { get; } = new();

    public RegistrationOptions Registration { get; } = new();

    public ApiAuthenticationOptions ApiAuth { get; } = new();

    public StorageOptions Storage { get; } = new();

    public NotificationOptions Notifications { get; } = new();

    public BackgroundJobOptions Jobs { get; } = new();

    public SettingsSection Settings { get; } = new();

    public string? SetupToken { get; set; }
}
