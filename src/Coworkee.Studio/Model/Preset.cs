namespace Coworkee.Studio.Model;

/// <summary>A starting point for the module and infrastructure options; the options page can change every value afterwards.</summary>
public sealed record Preset(string Name, string Description, string Icon, IReadOnlyDictionary<string, string> Values)
{
    private static readonly string[] ModuleSwitches =
        ["samples", "auditing", "notifications", "chat", "ai", "backup", "files", "features", "social", "extendedAttributes", "elasticsearch", "clientEntities", "keycloak",
         "registration", "githubActions", "devcontainer"];

    public static IReadOnlyList<Preset> All { get; } =
    [
        new("Minimal", "The shell with identity, permissions, settings and theming. Add modules later.", "Icon.Feather",
            Switches([], ("eventBus", "none"), ("storage", "fileSystem"))),
        new("Business app", "Audit log, notifications, files, backups and client entities for fast lists; events in process, a CI workflow.", "Icon.Briefcase",
            Switches(["auditing", "notifications", "backup", "files", "extendedAttributes", "clientEntities", "githubActions"], ("eventBus", "inProcess"), ("storage", "fileSystem"))),
        new("Collaboration", "Chat, notifications, files, comments and ratings, and the AI assistant for teams that work together.", "Icon.AccountGroup",
            Switches(["auditing", "notifications", "chat", "ai", "files", "social", "backup"], ("eventBus", "inProcess"), ("storage", "fileSystem"))),
        new("SaaS platform", "Tenants with features and editions, self registration, Keycloak sign-in, RabbitMQ events, Azure Blob storage and CI.", "Icon.Cloud",
            Switches(["auditing", "notifications", "features", "keycloak", "files", "ai", "registration", "githubActions"], ("eventBus", "rabbitMq"), ("storage", "azureBlob"))),
        new("Everything", "Every module and every container: search, RabbitMQ, Azurite, Keycloak, the samples, CI and a dev container.", "Icon.Rocket",
            Switches([.. ModuleSwitches], ("eventBus", "rabbitMq"), ("storage", "azureBlob"))),
    ];

    private static Dictionary<string, string> Switches(string[] on, params (string Id, string Value)[] choices)
    {
        var values = ModuleSwitches.ToDictionary(id => id, id => on.Contains(id) ? "true" : "false");
        foreach (var (id, value) in choices)
        {
            values[id] = value;
        }

        return values;
    }
}
