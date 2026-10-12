namespace Coworkee.Cli.Spec;

public enum OptionKind
{
    Bool,
    Choice,
    Text,

    /// <summary>#rrggbb.</summary>
    Color,

    /// <summary>A local file the CLI copies into the app (an http(s) address is passed as it is where noted).</summary>
    File,

    /// <summary>Several of the choices, comma separated (a JSON array in the spec).</summary>
    Multi,
}

public sealed record OptionChoice(string Value, string Description);

/// <summary>
/// One switch of <c>coworkee new</c>. <see cref="TemplateSymbol"/> options go to <c>dotnet new coworkee</c> as <c>--{Id}</c>;
/// the others are handled by the CLI itself.
/// </summary>
public sealed record CliOption(
    string Id,
    string Group,
    string Title,
    string Description,
    OptionKind Kind,
    string? Default,
    string WhenToUse,
    IReadOnlyList<string> Pros,
    IReadOnlyList<string> Cons,
    IReadOnlyList<OptionChoice>? Choices = null,
    string? Package = null,
    bool TemplateSymbol = true)
{
    /// <summary>The flag of <c>coworkee new</c>: the id in kebab case, e.g. <c>--event-bus</c>.</summary>
    public string Flag => "--" + string.Concat(Id.Select(c => char.IsUpper(c) ? "-" + char.ToLowerInvariant(c) : c.ToString()));
}

public static class OptionCatalog
{
    public const string General = "General";
    public const string Modules = "Modules";
    public const string Infrastructure = "Infrastructure";
    public const string Branding = "Branding";
    public const string SignIn = "Sign-in";
    public const string Code = "Code";
    public const string Tooling = "Tooling";

    /// <summary>Cultures the library ships texts for (Localization/{culture}.json of its modules), English being the keys.</summary>
    public static readonly IReadOnlyList<OptionChoice> Languages = [new("en", "English"), new("de", "German (Deutsch)")];

    public static readonly IReadOnlyList<string> Themes = ["Ocean", "Coworkee", "Classic", "Forest", "Midnight", "Sunset", "HighContrast"];

    public static IReadOnlyList<CliOption> All { get; } =
    [
        new("title", General, "Title", "Display title in the browser, the app bar, the sign-in pages and the Aspire dashboard (default: the name).", OptionKind.Text, null,
            "Always, when the name is technical (Shop) and users should read something else (Acme Shop).", ["Free text with spaces"], ["None"]),
        new("samples", General, "Sample modules", "Catalog (products, brands, dashboard) and Documents modules with their pages, tests and demo data.", OptionKind.Bool, "true",
            "To learn the patterns or to demo the library; switch off for a real app.", ["Working examples of every pattern", "Demo data after the first start"], ["Code you delete later"]),
        new("tests", General, "Test projects", "API, migration, app host and page tests.", OptionKind.Bool, "true",
            "Almost always; leave out only for throw-away prototypes.", ["Tests that boot the real app", "A model test that catches missing migrations"], ["Need Docker to run"]),
        new("git", General, "Git repository", "Run git init in the new folder.", OptionKind.Bool, "false",
            "When the app becomes its own repository.", ["Ready for the first commit"], ["Not wanted inside an existing repository"], TemplateSymbol: false),

        new("auditing", Modules, "Audit log", "Pages for the audit trail, entity history and restore. The trail itself is always written.", OptionKind.Bool, "true",
            "Whenever someone needs to see who changed what.", ["History and restore per entity"], ["More admin pages"], Package: "Coworkee.Auditing"),
        new("notifications", Modules, "Notifications", "In-app notifications with mail digests.", OptionKind.Bool, "true",
            "When users should hear about changes (comments, chat, own events).", ["Bell with live updates", "Digest mails"], ["Tables and a background job"], Package: "Coworkee.Notifications"),
        new("chat", Modules, "Chat", "Direct and group chat with realtime delivery.", OptionKind.Bool, "true",
            "For apps where users work together.", ["Built-in communication"], ["Not every app needs it"], Package: "Coworkee.Chat"),
        new("reports", Modules, "Reports", "Dashboards over the OData entity sets with charts, KPIs, tables and filters, aggregated on the server.", OptionKind.Bool, "true",
            "For apps whose users want to see their numbers.", ["Users build their own dashboards", "Shared with roles"], ["A designer page and two tables"], Package: "Coworkee.Reporting"),
        new("ai", Modules, "AI assistant", "Assistant page, MCP endpoint and tool call audit; every permitted command becomes a tool (provider and key in the settings).", OptionKind.Bool, "true",
            "When users or agents should work with the app in natural language.", ["Tools come from the existing commands", "MCP endpoint for Claude and other clients"], ["Needs an AI provider to answer"], Package: "Coworkee.Ai"),
        new("backup", Modules, "Database backups", "Backups and restore of the database from the admin pages.", OptionKind.Bool, "true",
            "Self-hosted installations without managed database backups.", ["Backup and restore without a DBA"], ["Managed databases bring their own"], Package: "Coworkee.Backup"),
        new("files", Modules, "Files", "Folders and files of the organisation with preview, upload and per-folder permissions.", OptionKind.Bool, "false",
            "When users share documents like on a drive.", ["File manager with preview of nearly every type"], ["Blob storage grows"], Package: "Coworkee.Files"),
        new("features", Modules, "Features and editions", "Features switched per tenant, editions and the tenant administration of the host.", OptionKind.Bool, "false",
            "Multi-tenant SaaS with plans.", ["[RequiresFeature] and feature gates"], ["Only useful with several tenants"], Package: "Coworkee.Features"),
        new("social", Modules, "Comments, tags and ratings", "Comments, tags and star ratings for any entity, opted in per entity type.", OptionKind.Bool, "false",
            "Content that people discuss or rate.", ["Ready-made components"], ["Each entity type has to be registered"], Package: "Coworkee.Social"),
        new("extendedAttributes", Modules, "Extended attributes", "Free key/value attributes for any entity without schema changes (the Documents sample brings it anyway).", OptionKind.Bool, "false",
            "When users need fields you cannot foresee.", ["No migration per field"], ["Values are not typed columns"], Package: "Coworkee.ExtendedAttributes"),
        // not "search": dotnet new reads --search as its own command
        new("elasticsearch", Modules, "Search", "Elasticsearch index; the app host adds an Elasticsearch container.", OptionKind.Bool, "false",
            "Full text search over many documents.", ["Facets, ranking, cursor paging"], ["Elasticsearch needs memory", "The app decides what to index"], Package: "Coworkee.Search.Elasticsearch"),
        new("eventBus", Modules, "Event bus", "Integration events through the transactional outbox.", OptionKind.Choice, "none",
            "When several apps or services react to each other's events.", ["Reliable delivery through the outbox", "Idempotent handlers through the inbox"], ["RabbitMQ is one more container"],
            [new("none", "No event bus."), new("inProcess", "Outbox and inbox, handlers in this app."), new("rabbitMq", "RabbitMQ transport, container in the app host.")], "Coworkee.EventBus"),
        new("clientEntities", Modules, "Client entities", "The browser keeps copies of OData sets marked [ClientEntity] and filters them locally. Entities of the spec with clientEntity switch it on.", OptionKind.Bool, "false",
            "Lists users filter all day, up to some ten thousand rows.", ["Instant filtering, sorting, facets", "Fewer queries on the server"], ["Rows are kept on the device", "Not for millions of rows"], Package: "Coworkee.Client.Blazor.ClientEntities"),

        new("keycloak", Infrastructure, "Keycloak", "Keycloak container as external sign-in, with a realm for the app.", OptionKind.Bool, "false",
            "When users should sign in with an identity provider.", ["Single sign-on ready to try"], ["One more container"]),
        new("storage", Infrastructure, "Blob storage", "Where uploaded files and backups go.", OptionKind.Choice, "fileSystem",
            "Azure Blob when the app will run on Azure.", ["Same code for every provider"], ["Azurite is one more container"],
            [new("fileSystem", "Folder .data/blobs next to src."), new("azureBlob", "Azure Blob Storage, Azurite emulator in the app host.")], "Coworkee.Storage"),
        new("aiProvider", Infrastructure, "AI provider", "Model provider of the assistant (with the AI assistant); changeable later on the AI page of the app.", OptionKind.Choice, "anthropic",
            "Anthropic, OpenAI, Gemini or Mistral with a key; OpenRouter for many models behind one key; Ollama or LM Studio to run locally without a key; Azure OpenAI when data must stay in your Azure tenant; openAICompatible for gateways and vLLM.",
            ["Same tools and audit with every provider", "Ollama runs offline in a container"], ["Small local models call tools less reliably", "Ollama needs several GB of disk and RAM"],
            [.. Contracts.Ai.AiProviderCatalog.All.Select(p => new OptionChoice(p.Id, p.Notes))]),
        new("aiModel", Infrastructure, "AI model", "Model of the AI provider (Azure OpenAI: the deployment name); empty takes the provider's default. The key never goes into the spec: user secrets of the app host, Parameters:ai-apikey.", OptionKind.Text, null,
            "When the default model of the provider does not fit, or it has none (Azure OpenAI, LM Studio, openAICompatible).", ["Changeable later on the AI page"], ["None"]),
        new("aiEndpoint", Infrastructure, "AI endpoint", "Base address of the AI provider, for Azure OpenAI, openAICompatible or a provider behind a proxy; empty takes the provider's address.", OptionKind.Text, null,
            "With Azure OpenAI (https://<resource>.openai.azure.com) or an OpenAI compatible server.", ["Changeable later on the AI page"], ["None"]),
        new("redis", Infrastructure, "Redis", "Redis container in the app host: SignalR backplane, shared presence, cache invalidation and sign-in sessions across instances.", OptionKind.Bool, "true",
            "As soon as an api or the web app runs in more than one instance; keep it for production-like development.",
            ["Scale out without sticky state", "Permission and setting changes reach every instance at once", "Sign-in sessions survive restarts of the web app"],
            ["One more container", "Without it everything stays in each process, fine for a single instance"]),
        new("grafana", Infrastructure, "Grafana", "Grafana with Prometheus, Loki, Tempo and an OpenTelemetry collector in the app host: metrics, logs and traces of every service, the app database and ready dashboards. Sign-in with the app's administrator, and Keycloak when it is on.", OptionKind.Bool, "true",
            "Whenever you want to see how the app behaves: request rates, errors, latency, runtime, database and users at a glance.",
            ["Dashboards for requests, runtime, database and the app's data from the first start", "Metrics, logs and traces side by side and kept across sessions of the Aspire dashboard", "Same sign-in as the app"],
            ["Five more containers (Grafana, Prometheus, Loki, Tempo, collector)", "Development only: a deployment brings its own monitoring"]),
        new("webDataStudio", Infrastructure, "WebDataStudio", "WebDataStudio, a database studio in the browser, connected to the app database. Sign-in with the app's administrator, and Keycloak when it is on.", OptionKind.Bool, "true",
            "Whenever you want to look into the app database, run SQL or fix data while developing.",
            ["Tables, queries, charts and exports without a local database tool", "Same sign-in as the app"],
            ["One more container", "Development only: it has full access to the database"]),
        new("database", Infrastructure, "Database", "Database engine of the app; the app host starts it and the initial migration is made for it.", OptionKind.Choice, "postgres",
            "Postgres unless the organisation runs SQL Server (or Azure SQL); SQLite for demos, prototypes and installs on one machine.",
            ["postgres: every module, database backups from the admin pages", "sqlServer: fits Microsoft shops and Azure SQL", "sqlite: no container, the database is one file"],
            ["sqlServer: no backups from the admin pages, a heavy container", "sqlite: one writer at a time, background jobs kept in memory, no backups from the admin pages"],
            [new("postgres", "PostgreSQL container in the app host."), new("sqlServer", "SQL Server container in the app host."), new("sqlite", "SQLite file .data/<name>.db, no container.")]),

        new("theme", Branding, "Theme", "Default theme of the installation.", OptionKind.Choice, "Ocean",
            "Pick the look closest to your brand; brand colors adjust it.", ["Seven themes that differ in more than colors"], ["Organisations can still choose another"],
            [new("Ocean", "Blue, humanist font, rounded."), new("Coworkee", "Warm orange on paper."), new("Classic", "MudBlazor purple."), new("Forest", "Green with serif headings."),
             new("Midnight", "Indigo, monospace headings."), new("Sunset", "Pink, rounded, roomy."), new("HighContrast", "Black and white, no shadows.")]),
        new("primaryColor", Branding, "Primary color", "Primary brand color #rrggbb. With a brand color the app gets its own theme (named like the title), based on the chosen theme, as default.", OptionKind.Color, null,
            "When the app should carry the brand color.", ["Contrast text is computed"], ["The title must not be the name of a built-in theme"]),
        new("secondaryColor", Branding, "Secondary color", "Secondary brand color #rrggbb.", OptionKind.Color, null,
            "Together with the primary color.", ["Accent in buttons and links"], ["None"]),
        new("logo", Branding, "Logo", "Logo file (svg, png, ...); the CLI copies it into the web client. It shows in the app bar, the browser tab and on the sign-in pages.", OptionKind.File, "coworkee-icon.svg",
            "Always for a real app.", ["One file for app, tab and sign-in"], ["SVG scales best"]),

        new("signInLayout", SignIn, "Sign-in layout", "Layout of the sign-in pages.", OptionKind.Choice, "Slide",
            "Split for a full-page image or video, Card for a plain look.", ["Changeable later on the Themes page"], ["None"],
            [new("Card", "A centered card."), new("Split", "Image or video on one side, the form on the other."), new("Slide", "A colored panel next to the form.")]),
        new("signInMedia", SignIn, "Sign-in media", "Image or video next to the sign-in form: a local file (copied into the web client) or an http(s) address.", OptionKind.File, null,
            "With the Split or Slide layout.", [".mp4, .webm, .ogg and .mov play as video"], ["Large videos slow the first sign-in"]),
        new("signInMediaSide", SignIn, "Sign-in media side", "Side of the sign-in media.", OptionKind.Choice, "Left",
            "To match the media's composition.", ["None"], ["None"], [new("Left", "Media left."), new("Right", "Media right.")]),

        new("mapping", Code, "DTO mapping", "Default mapping of generated entities to their DTOs; an entity can choose its own.", OptionKind.Choice, "manual",
            "Mapperly for many or large entities.", ["manual: plain code you can read", "mapperly: compile-time mapper, reports unmapped members"], ["manual: one more line per property", "mapperly: one more package"],
            [new("manual", "A ToDto extension method."), new("mapperly", "A Mapperly mapper.")], TemplateSymbol: false),

        // languages, color mode, registration and repository tooling
        new("languages", General, "Languages", "Languages the app offers from the start, from those the library ships texts for; administrators add more later on the Languages page. Empty offers all of them.", OptionKind.Multi, null,
            "When the users speak only some of the languages.", ["Users see only languages that make sense"], ["More languages later need the Languages page or a translator key"], Languages),
        new("defaultLanguage", General, "Default language", "Language of users who chose none and whose browser asks for none of the offered ones.", OptionKind.Choice, "en",
            "When most users speak another language than English.", ["Right language at the first visit"], ["Must be one of the languages"], Languages),
        new("colorMode", General, "Color mode", "Light, dark or the system's mode as default of the installation; every user can switch it in the app bar.", OptionKind.Choice, "system",
            "Dark for apps used in dim rooms or all day.", ["Seeded as system wide setting, changeable later"], ["None"],
            [new("system", "Follow the operating system."), new("light", "Always light."), new("dark", "Always dark.")]),
        new("registration", General, "Self registration", "Visitors can register on the sign-in page (setting Account.AllowRegistration, changeable later).", OptionKind.Bool, "true",
            "Public apps where anyone may sign up; leave off when administrators invite users.", ["No invitation needed"], ["Anyone with the address can get an account"]),
        new("githubActions", Tooling, "GitHub Actions", "A workflow (.github/workflows/ci.yml) that builds the solution and runs its tests on every push and pull request.", OptionKind.Bool, "false",
            "When the app lives on GitHub.", ["Broken builds show up in pull requests", "The tests run with Docker on the runner"], ["Minutes of the GitHub plan"]),
        new("devcontainer", Tooling, "Dev container", "A .devcontainer with the .NET SDK and Docker in Docker for GitHub Codespaces and VS Code.", OptionKind.Bool, "false",
            "When people should start without installing anything.", ["Same tools for everyone", "Runs in GitHub Codespaces"], ["The container image is large"]),
    ];

    public static CliOption? Find(string id) => All.FirstOrDefault(o => string.Equals(o.Id, id, StringComparison.OrdinalIgnoreCase));
}
