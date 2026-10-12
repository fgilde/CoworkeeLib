namespace Coworkee.Studio.Model;

/// <summary>A starting point for the module and infrastructure options; the options page can change every value afterwards.</summary>
public sealed record Preset(string Name, string Description, string Icon, IReadOnlyDictionary<string, string> Values)
{
    private static readonly string[] ModuleSwitches =
        ["samples", "auditing", "notifications", "chat", "reports", "ai", "backup", "files", "features", "social", "extendedAttributes", "elasticsearch", "clientEntities", "keycloak",
         "registration", "githubActions", "devcontainer", "redis", "grafana", "webDataStudio"];

    public static IReadOnlyList<Preset> All { get; } =
    [
        new("Minimal", "The shell with identity, permissions, settings and theming on Postgres. No Redis: fine while one instance runs. Add modules later.", "Icon.Feather",
            Switches(["registration"], ("eventBus", "none"), ("storage", "fileSystem"), ("translationProvider", "none"))),
        new("Business app", "Audit log, notifications, reports, files, backups and client entities for fast lists on Postgres; Redis to scale out, events in process, a CI workflow.", "Icon.Briefcase",
            Switches(["auditing", "notifications", "reports", "backup", "files", "extendedAttributes", "clientEntities", "registration", "githubActions", "redis", "grafana", "webDataStudio"], ("eventBus", "inProcess"), ("storage", "fileSystem"))),
        new("Collaboration", "Chat, notifications, files, comments and ratings, and the AI assistant with Claude; Redis carries chat and presence across instances.", "Icon.AccountGroup",
            Switches(["auditing", "notifications", "chat", "ai", "files", "social", "backup", "registration", "redis", "grafana", "webDataStudio"], ("eventBus", "inProcess"), ("storage", "fileSystem"))),
        new("SaaS platform", "Tenants with features and editions, self registration, Keycloak sign-in, Azure OpenAI, RabbitMQ events, Azure Blob storage, Redis and CI.", "Icon.Cloud",
            Switches(["auditing", "notifications", "reports", "features", "keycloak", "files", "ai", "registration", "githubActions", "redis", "grafana", "webDataStudio"], ("eventBus", "rabbitMq"), ("storage", "azureBlob"),
                ("aiProvider", "azureOpenAI"))),
        new("Local demo", "SQLite file, a local model in Ollama, no Redis: runs offline on one machine without keys. One writer at a time and no backups, so not for production.", "Icon.Monitor",
            Switches(["samples", "notifications", "chat", "reports", "ai", "files", "registration", "webDataStudio"], ("eventBus", "none"), ("storage", "fileSystem"), ("database", "sqlite"), ("aiProvider", "ollama"))),
        new("Everything", "Every module and every container: Postgres, Redis, Ollama, search, RabbitMQ, Azurite, Keycloak, the samples, CI and a dev container.", "Icon.Rocket",
            Switches([.. ModuleSwitches], ("eventBus", "rabbitMq"), ("storage", "azureBlob"), ("aiProvider", "ollama"))),
    ];

    private static Dictionary<string, string> Switches(string[] on, params (string Id, string Value)[] choices)
    {
        var values = ModuleSwitches.ToDictionary(id => id, id => on.Contains(id) ? "true" : "false");
        values["database"] = "postgres";
        values["aiProvider"] = "anthropic";
        values["translationProvider"] = "libreTranslate";
        foreach (var (id, value) in choices)
        {
            values[id] = value;
        }

        return values;
    }
}
