using Coworkee.Contracts.Realtime;

namespace Coworkee.Client.Blazor.Components;

/// <summary>A saved app configuration value: the server sends its key (never the value) on this topic to whoever may manage the settings.</summary>
internal static class AppConfigurationChanges
{
    public static readonly string Topic = RealtimeTopics.Type("ConfigurationEntry");

    /// <summary>Whether the event changed a value of <paramref name="section"/> and not of a section inside it (Coworkee:Auth:Design inside Coworkee).</summary>
    public static bool Touches(RealtimeEnvelope envelope, string section, IEnumerable<ClientAppConfiguration> registrations) =>
        EntityChangedPayload.From(envelope)?.EntityId is { } key && Within(key, section)
        && !registrations.Select(r => r.Section).Append(Contracts.Theming.SignInDesign.Section) // the sign-in design has its own card, not a tab under Settings
            .Any(other => other.Length > section.Length && Within(other, section) && Within(key, other));

    private static bool Within(string key, string section) => key.StartsWith(section + ":", StringComparison.OrdinalIgnoreCase);
}
