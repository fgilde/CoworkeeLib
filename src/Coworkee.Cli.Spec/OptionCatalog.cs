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

/// <summary>One choice of a choice option; <paramref name="InfoUrl"/> is the website of the product it stands for.</summary>
public sealed record OptionChoice(string Value, string Description, string? InfoUrl = null);

/// <summary>
/// An option that only matters while another one has one of <paramref name="Values"/> (none: while it is set, a bool being true),
/// or while <see cref="Or"/> is met.
/// </summary>
public sealed record OptionDependency(string Option, params string[] Values)
{
    public OptionDependency? Or { get; init; }

    public bool IsMet(string? value) => Values.Length == 0 ? value is not (null or "" or "false") : value is not null && Values.Contains(value);

    public override string ToString() => (Values.Length == 0 ? Option : $"{Option} = {string.Join(" or ", Values)}") + (Or is { } other ? $", or {other}" : string.Empty);
}

/// <summary>
/// One switch of <c>coworkee new</c>. <see cref="TemplateSymbol"/> options go to <c>dotnet new coworkee</c> as <c>--{Id}</c>;
/// the others are handled by the CLI itself. <see cref="InfoUrl"/> links to more about what the option brings;
/// <see cref="DependsOn"/> names the option it needs: without it this one is neither asked for nor used.
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
    bool TemplateSymbol = true,
    string? InfoUrl = null,
    OptionDependency? DependsOn = null)
{
    /// <summary>The flag of <c>coworkee new</c>: the id in kebab case, e.g. <c>--event-bus</c>.</summary>
    public string Flag => "--" + string.Concat(Id.Select((c, i) => char.IsUpper(c) ? (i > 0 && char.IsLower(Id[i - 1]) ? "-" : "") + char.ToLowerInvariant(c) : c.ToString()));
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

    /// <summary>Cultures the library ships texts for (Localization/{culture}.json of its modules), English being the keys; apps start with <see cref="DefaultLanguages"/>.</summary>
    public static readonly IReadOnlyList<string> ShippedLanguages =
        ["en", "ar", "bg", "cs", "da", "de", "el", "es", "fr", "hr", "id", "it", "ja", "km", "ko", "nl", "pl", "pt", "ru", "sv", "tr", "uk", "zh"];

    /// <summary>The languages an app offers when none are chosen; administrators switch on the other shipped ones later.</summary>
    public static readonly IReadOnlyList<string> DefaultLanguages = ["en", "de"];

    /// <summary>Languages to choose from, by their own name: the shipped ones and those the translation services (LibreTranslate included) translate into.</summary>
    public static readonly IReadOnlyList<OptionChoice> Languages =
    [
        new("en", "English"), new("de", "Deutsch"), new("fr", "Français"), new("es", "Español"), new("it", "Italiano"), new("pt", "Português"),
        new("pt-BR", "Português (Brasil)"), new("nl", "Nederlands"), new("pl", "Polski"), new("cs", "Čeština"), new("sk", "Slovenčina"), new("sl", "Slovenščina"),
        new("sv", "Svenska"), new("da", "Dansk"), new("nb", "Norsk bokmål"), new("fi", "Suomi"), new("et", "Eesti"), new("lv", "Latviešu"), new("lt", "Lietuvių"),
        new("hu", "Magyar"), new("ro", "Română"), new("bg", "Български"), new("el", "Ελληνικά"), new("tr", "Türkçe"), new("ru", "Русский"), new("uk", "Українська"),
        new("ar", "العربية"), new("he", "עברית"), new("hi", "हिन्दी"), new("ja", "日本語"), new("ko", "한국어"), new("zh", "中文"), new("zh-Hans", "中文（简体）"), new("zh-Hant", "中文（繁體）"),
        new("id", "Bahasa Indonesia"), new("vi", "Tiếng Việt"), new("th", "ไทย"), new("hr", "Hrvatski"), new("km", "ភាសាខ្មែរ"),
    ];

    /// <summary>The options whose choices are <see cref="Languages"/>, named in their own language.</summary>
    public static readonly IReadOnlyList<string> LanguageOptions = ["languages", "defaultLanguage", "translationSource"];

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
            "When the app becomes its own repository.", ["Ready for the first commit"], ["Not wanted inside an existing repository"], TemplateSymbol: false, InfoUrl: "https://git-scm.com/"),
        new("airworks", General, "AirWorks client", "A second client next to the Blazor app: the AirWorks web desktop (TypeScript) with windows, taskbar and apps for nearly everything the Blazor app offers: settings, files, data, chat, reports with their designer, the assistant and the administration. Same sign-in (and Keycloak), same api, theme and logo of the app; runs in the Aspire app host.", OptionKind.Bool, "false",
            "When users want a desktop-like workspace with several windows side by side, or the app needs a TypeScript client to extend.",
            ["Library apps for settings, files with preview, data, chat, reports and the administration", "Your own apps in TypeScript next to them", "Same users, permissions and data as the Blazor app"],
            ["Needs Node.js to build", "A second client to keep up to date"], InfoUrl: "https://fgilde.github.io/AirWorks/"),
        new("airworksPackages", General, "AirWorks packages", "Where the AirWorks client takes @coworkee/client and @coworkee/airworks from: the tarballs inside the template (no npm registry needed for them) or npm in the version of the template.", OptionKind.Choice, "bundled",
            "npm once the packages of the release are on npm and the app should update them like any other npm dependency.",
            ["Bundled: works offline and right after a release", "npm: updates like other npm dependencies"], ["Bundled: the tarballs live in the app's repository", "npm: needs the release on npmjs.com"],
            [new("bundled", "The tarballs inside the template, in ClientApp/vendor."), new("npm", "npm, the version of this template.", "https://www.npmjs.com/")], DependsOn: new("airworks")),
        new("pwa", General, "Installable app", "Web manifest, service worker and offline page: the app installs on phones and desktops and opens without the browser bar. With notifications users switch on Web Push per device.", OptionKind.Bool, "true",
            "Almost always; off only when the app must not run as installed app.", ["Installs from the browser like a native app", "Push notifications on phones and desktops", "Asks to reload after each deployment"],
            ["A deployment needs its own VAPID key pair for push"], InfoUrl: "https://web.dev/explore/progressive-web-apps"),
        new("repository", General, "GitHub repository", "GitHub repository of the app as owner/repo, e.g. acme/shop: the README gets a QuickRun badge that starts the app from there with one click.", OptionKind.Text, null,
            "When the app lives on GitHub and others should try it.", ["One click from the README to the running app"], ["GitHub only"], InfoUrl: "https://github.com/"),

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
            "Self-hosted installations without managed database backups.", ["Backup and restore without a DBA"], ["Managed databases bring their own"], Package: "Coworkee.Backup", DependsOn: new("database", "postgres")),
        new("files", Modules, "Files", "Folders and files of the organisation with preview, upload and per-folder permissions.", OptionKind.Bool, "false",
            "When users share documents like on a drive.", ["File manager with preview of nearly every type"], ["Blob storage grows"], Package: "Coworkee.Files"),
        new("features", Modules, "Features and editions", "Features switched per tenant, editions and the tenant administration of the host.", OptionKind.Bool, "false",
            "Multi-tenant SaaS with plans.", ["[RequiresFeature] and feature gates"], ["Only useful with several tenants"], Package: "Coworkee.Features"),
        new("social", Modules, "Comments, tags and ratings", "Comments, tags and star ratings for any entity, opted in per entity type.", OptionKind.Bool, "false",
            "Content that people discuss or rate.", ["Ready-made components"], ["Each entity type has to be registered"], Package: "Coworkee.Social"),
        new("workflows", Modules, "Workflows and approvals", "States and transitions per entity type, approvals by roles or users with deadlines, reminders and escalation, a history, the approvals inbox; declared in code. With the samples, products go through an approval.", OptionKind.Bool, "false",
            "When entities move through steps that people decide on, such as review and release.", ["Guards by permission, role or condition", "Buttons, timeline and inbox ready-made"], ["Each entity type has to be declared"], Package: "Coworkee.Workflows"),
        new("elsa", Modules, "Elsa workflows", "Elsa Workflows 3 in the api: its tables in the app's database, its api under /api/elsa for the permission Workflows.Elsa.Manage, activities and triggers for Coworkee workflows. Elsa Studio runs as its own app.", OptionKind.Bool, "false",
            "When administrators should build long running processes themselves, beyond the states declared in code.", ["Visual designer (Elsa Studio)", "Starts from transitions, decisions and entity changes"], ["Elsa brings many packages and its own tables", "Elsa Studio is a separate app to run"],
            Package: "Coworkee.Workflows.Elsa", InfoUrl: "https://elsa-workflows.github.io/elsa-documentation/", DependsOn: new("workflows")),
        new("extendedAttributes", Modules, "Extended attributes", "Free key/value attributes for any entity without schema changes (the Documents sample brings it anyway).", OptionKind.Bool, "false",
            "When users need fields you cannot foresee.", ["No migration per field"], ["Values are not typed columns"], Package: "Coworkee.ExtendedAttributes"),
        // not "search": dotnet new reads --search as its own command
        new("elasticsearch", Modules, "Search", "Elasticsearch index; the app host adds an Elasticsearch container.", OptionKind.Bool, "false",
            "Full text search over many documents.", ["Facets, ranking, cursor paging"], ["Elasticsearch needs memory", "The app decides what to index"], Package: "Coworkee.Search.Elasticsearch", InfoUrl: "https://www.elastic.co/elasticsearch"),
        new("eventBus", Modules, "Event bus", "Integration events through the transactional outbox.", OptionKind.Choice, "none",
            "When several apps or services react to each other's events.", ["Reliable delivery through the outbox", "Idempotent handlers through the inbox"], ["RabbitMQ is one more container"],
            [new("none", "No event bus."), new("inProcess", "Outbox and inbox, handlers in this app."), new("rabbitMq", "RabbitMQ transport, container in the app host.", "https://www.rabbitmq.com/")], "Coworkee.EventBus"),
        new("clientEntities", Modules, "Client entities", "The browser keeps copies of OData sets marked [ClientEntity] and filters them locally. Entities of the spec with clientEntity switch it on.", OptionKind.Bool, "false",
            "Lists users filter all day, up to some ten thousand rows.", ["Instant filtering, sorting, facets", "Fewer queries on the server"], ["Rows are kept on the device", "Not for millions of rows"], Package: "Coworkee.Client.Blazor.ClientEntities"),

        new("keycloak", Infrastructure, "Keycloak", "Keycloak container as external sign-in, with a realm for the app.", OptionKind.Bool, "false",
            "When users should sign in with an identity provider.", ["Single sign-on ready to try"], ["One more container"], InfoUrl: "https://www.keycloak.org/"),
        new("storage", Infrastructure, "Blob storage", "Where uploaded files and backups go.", OptionKind.Choice, "fileSystem",
            "Azure Blob when the app will run on Azure.", ["Same code for every provider"], ["Azurite is one more container"],
            [new("fileSystem", "Folder .data/blobs next to src."), new("azureBlob", "Azure Blob Storage, Azurite emulator in the app host.", "https://azure.microsoft.com/products/storage/blobs")], "Coworkee.Storage"),
        new("aiProvider", Infrastructure, "AI provider", "Model provider of the assistant (with the AI assistant); changeable later on the AI page of the app.", OptionKind.Choice, "anthropic",
            "Anthropic, OpenAI, Gemini or Mistral with a key; OpenRouter for many models behind one key; Ollama or LM Studio to run locally without a key; Azure OpenAI when data must stay in your Azure tenant; openAICompatible for gateways and vLLM.",
            ["Same tools and audit with every provider", "Ollama runs offline in a container"], ["Small local models call tools less reliably", "Ollama needs several GB of disk and RAM"],
            [.. Contracts.Ai.AiProviderCatalog.All.Select(p => new OptionChoice(p.Id, p.Notes, p.Local ? p.KeyUrl : null))], DependsOn: new("ai")),
        new("aiModel", Infrastructure, "AI model", "Model of the AI provider (Azure OpenAI: the deployment name); empty takes the provider's default. The key never goes into the spec: user secrets of the app host, Parameters:ai-apikey.", OptionKind.Text, null,
            "When the default model of the provider does not fit, or it has none (Azure OpenAI, LM Studio, openAICompatible).", ["Changeable later on the AI page"], ["None"], DependsOn: new("ai")),
        new("aiEndpoint", Infrastructure, "AI endpoint", "Base address of the AI provider, for Azure OpenAI, openAICompatible or a provider behind a proxy; empty takes the provider's address.", OptionKind.Text, null,
            "With Azure OpenAI (https://<resource>.openai.azure.com) or an OpenAI compatible server.", ["Changeable later on the AI page"], ["None"], DependsOn: new("ai")),
        new("redis", Infrastructure, "Redis", "Redis container in the app host: SignalR backplane, shared presence, cache invalidation and sign-in sessions across instances.", OptionKind.Bool, "true",
            "As soon as an api or the web app runs in more than one instance; keep it for production-like development.",
            ["Scale out without sticky state", "Permission and setting changes reach every instance at once", "Sign-in sessions survive restarts of the web app"],
            ["One more container", "Without it everything stays in each process, fine for a single instance"], InfoUrl: "https://redis.io/"),
        new("grafana", Infrastructure, "Grafana", "Grafana with Prometheus, Loki, Tempo and an OpenTelemetry collector in the app host: metrics, logs and traces of every service, the app database and ready dashboards. Sign-in with the app's administrator, and Keycloak when it is on.", OptionKind.Bool, "true",
            "Whenever you want to see how the app behaves: request rates, errors, latency, runtime, database and users at a glance.",
            ["Dashboards for requests, runtime, database and the app's data from the first start", "Metrics, logs and traces side by side and kept across sessions of the Aspire dashboard", "Same sign-in as the app"],
            ["Five more containers (Grafana, Prometheus, Loki, Tempo, collector)", "Development only: a deployment brings its own monitoring"], InfoUrl: "https://grafana.com/"),
        new("webDataStudio", Infrastructure, "WebDataStudio", "WebDataStudio, a database studio in the browser, connected to the app database. Sign-in with the app's administrator, and Keycloak when it is on.", OptionKind.Bool, "true",
            "Whenever you want to look into the app database, run SQL or fix data while developing.",
            ["Tables, queries, charts and exports without a local database tool", "Same sign-in as the app"],
            ["One more container", "Development only: it has full access to the database"], InfoUrl: "https://fgilde.github.io/WebDataStudio/"),
        new("aspireUI", Infrastructure, "AspireUI", "AspireUI, a web UI to host and try more apps and stacks next to the app, in the app host. Its apps show up on the admin page Services. Sign-in with the app's administrator, and Keycloak when it is on.", OptionKind.Bool, "false",
            "When the app should be tried next to other apps or stacks (a second service, a database tool, a sample), or the Services page should list them.",
            ["Start and stop more apps and stacks from one web UI", "The hosted apps show up on the admin page Services", "Same sign-in as the app"],
            ["One more container", "Development only: it hosts whatever you give it"], InfoUrl: "https://fgilde.github.io/AspireUI/"),
        new("database", Infrastructure, "Database", "Database engine of the app; the app host starts it and the initial migration is made for it.", OptionKind.Choice, "postgres",
            "Postgres unless the organisation runs SQL Server (or Azure SQL); SQLite for demos, prototypes and installs on one machine.",
            ["postgres: every module, database backups from the admin pages", "sqlServer: fits Microsoft shops and Azure SQL", "sqlite: no container, the database is one file"],
            ["sqlServer: no backups from the admin pages, a heavy container", "sqlite: one writer at a time, background jobs kept in memory, no backups from the admin pages"],
            [new("postgres", "PostgreSQL container in the app host.", "https://www.postgresql.org/"), new("sqlServer", "SQL Server container in the app host.", "https://www.microsoft.com/sql-server"), new("sqlite", "SQLite file .data/<name>.db, no container.", "https://sqlite.org/")]),

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
            "With the Split or Slide layout, or as background of the loading screen.", [".mp4, .webm, .ogg and .mov play as video"], ["Large videos slow the first sign-in"],
            DependsOn: new("signInLayout", "Split", "Slide") { Or = new("loadingScreen", "SignInMedia") }),
        new("signInMediaSide", SignIn, "Sign-in media side", "Side of the sign-in media.", OptionKind.Choice, "Left",
            "To match the media's composition.", ["None"], ["None"], [new("Left", "Media left."), new("Right", "Media right.")], DependsOn: new("signInMedia")),
        new("loadingScreen", SignIn, "Loading screen", "Background of the app's loading screen while the WebAssembly app loads.", OptionKind.Choice, "Animated",
            "SignInMedia to greet with the sign-in image or video already while loading.", ["Changeable later on the Themes page"], ["None"],
            [new("Animated", "Soft moving shapes in the theme's colours."), new("SignInMedia", "The image or video of the sign-in pages."), new("Plain", "The theme's background colour only.")]),

        new("mapping", Code, "DTO mapping", "Default mapping of generated entities to their DTOs; an entity can choose its own.", OptionKind.Choice, "manual",
            "Mapperly for many or large entities.", ["manual: plain code you can read", "mapperly: compile-time mapper, reports unmapped members"], ["manual: one more line per property", "mapperly: one more package"],
            [new("manual", "A ToDto extension method."), new("mapperly", "A Mapperly mapper.", "https://mapperly.riok.app/")], TemplateSymbol: false),

        // languages, color mode, registration and repository tooling
        new("languages", General, "Languages", "Languages the app offers from the start. 23 languages ship with the library; the others are machine translated at the first start with the translation service. Administrators switch more on later on the Languages page. Empty offers English and German.", OptionKind.Multi, null,
            "When the users speak only some of the languages, or languages other than English and German.", ["Users see only languages that make sense", "Any language the translation service knows"], ["Machine translations are good, not perfect: review them on the Translations page"], Languages),
        new("defaultLanguage", General, "Default language", "Language of users who chose none and whose browser asks for none of the offered ones.", OptionKind.Choice, "en",
            "When most users speak another language than English.", ["Right language at the first visit"], ["Must be one of the languages"], Languages),
        new("translationProvider", General, "Translation service", "Translates the texts a language lacks: at the first start, when a language is switched on, or on request. Changeable later in the settings (Translation).", OptionKind.Choice, "libreTranslate",
            "LibreTranslate to start free and without an account, DeepL or Azure for the best quality, AI when the assistant's model should do it.",
            ["libreTranslate: free, no account, the texts stay on your machine", "myMemory: free, no container, nothing to install", "deepL, azure, google: high quality, free monthly quota with an account"],
            ["libreTranslate: a container of about 600 MB plus about 100 MB per language, the first start waits for the download", "myMemory: 5,000 characters a day (50,000 with an email), too little for a whole language at once", "deepL, azure, google, ai: a key in the user secrets, texts leave the machine"],
            [new("libreTranslate", "LibreTranslate container in the app host, free and without an account.", "https://libretranslate.com/"), new("myMemory", "MyMemory public service, free without an account, small daily quota.", "https://mymemory.translated.net/"),
             new("deepL", "DeepL API, free plan up to 500,000 characters a month (key).", "https://www.deepl.com/pro-api"), new("azure", "Azure AI Translator, free tier up to 2 million characters a month (key, region).", "https://azure.microsoft.com/products/ai-services/ai-translator"),
             new("google", "Google Cloud Translation, 500,000 characters a month free (key).", "https://cloud.google.com/translate"), new("ai", "The model of the AI assistant."), new("none", "No machine translation; missing texts show in English.")]),
        new("translationSource", General, "Translation source", "The language translations start from. Texts are written in English; German starts from the shipped German wording where there is one.", OptionKind.Choice, "en",
            "German when your own texts are reviewed in German first.", ["Translations from your reviewed wording"], ["Only languages with shipped texts"], [new("en", "English"), new("de", "Deutsch")]),
        new("colorMode", General, "Color mode", "Light, dark or the system's mode as default of the installation; every user can switch it in the app bar.", OptionKind.Choice, "system",
            "Dark for apps used in dim rooms or all day.", ["Seeded as system wide setting, changeable later"], ["None"],
            [new("system", "Follow the operating system."), new("light", "Always light."), new("dark", "Always dark.")]),
        new("registration", General, "Self registration", "Visitors can register on the sign-in page (setting Account.AllowRegistration, changeable later).", OptionKind.Bool, "true",
            "Public apps where anyone may sign up; leave off when administrators invite users.", ["No invitation needed"], ["Anyone with the address can get an account"]),
        new("githubActions", Tooling, "GitHub Actions", "A workflow (.github/workflows/ci.yml) that builds the solution and runs its tests on every push and pull request.", OptionKind.Bool, "false",
            "When the app lives on GitHub.", ["Broken builds show up in pull requests", "The tests run with Docker on the runner"], ["Minutes of the GitHub plan"], InfoUrl: "https://github.com/features/actions"),
        new("devcontainer", Tooling, "Dev container", "A .devcontainer with the .NET SDK and Docker in Docker for GitHub Codespaces and VS Code.", OptionKind.Bool, "false",
            "When people should start without installing anything.", ["Same tools for everyone", "Runs in GitHub Codespaces"], ["The container image is large"], InfoUrl: "https://containers.dev/"),
    ];

    public static CliOption? Find(string id) => All.FirstOrDefault(o => string.Equals(o.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>The dependency that keeps the option from mattering (its own, or one of the option it depends on), or null when it matters.</summary>
    public static OptionDependency? Unmet(CliOption option, Func<string, string?> value) => option.DependsOn is { } need ? Unmet(need, value) : null;

    private static OptionDependency? Unmet(OptionDependency need, Func<string, string?> value)
    {
        var unmet = Find(need.Option) is { } parent && Unmet(parent, value) is { } root ? root : need.IsMet(value(need.Option)) ? null : need;
        return unmet is not null && need.Or is { } other && Unmet(other, value) is null ? null : unmet;
    }

    public static bool IsActive(CliOption option, Func<string, string?> value) => Unmet(option, value) is null;

    public static bool IsActive(CliOption option, IReadOnlyDictionary<string, string?> values) => IsActive(option, id => values[id]);
}
