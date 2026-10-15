using System.ComponentModel;
using System.Text.Json.Nodes;
using Coworkee.Cli.Commands;
using Coworkee.Cli.Spec;
using ModelContextProtocol.Server;

namespace Coworkee.Cli.Mcp;

/// <summary>Output and exit code of a coworkee command the MCP server ran.</summary>
internal sealed record CliRun(int ExitCode, string Output)
{
    public override string ToString() => $"exit code {ExitCode}\n{Output}";
}

/// <summary>Runs this CLI with arguments in a folder; the MCP server never writes command output to its own stdout, which carries the protocol.</summary>
internal delegate Task<CliRun> CliRunner(IReadOnlyList<string> args, string workingDirectory, CancellationToken cancellationToken);

[McpServerToolType]
internal sealed class CoworkeeMcpTools(CliRunner run, HttpClient http)
{
    [McpServerTool(Name = "list_options", ReadOnly = true, Title = "List the options of coworkee new")]
    [Description("Every option of a new Coworkee app (modules, infrastructure, branding, sign-in, code generation) with id, flag, kind, default, choices, pros, cons and when to use it. Use the ids in the options of a spec.")]
    public static string ListOptions([Description("Only this group: General, Modules, Infrastructure, Branding, Sign-in or Code.")] string? group = null) =>
        OptionsCommand.CatalogJson(group);

    [McpServerTool(Name = "get_spec_schema", ReadOnly = true, Title = "JSON Schema of an app spec")]
    [Description("JSON Schema of the app spec that create_app takes: name, options, branding, modules with entities and their properties, roles and users.")]
    public static string GetSpecSchema() => SpecJson.Schema().ToJsonString(SpecJson.Options);

    [McpServerTool(Name = "get_spec_example", ReadOnly = true, Title = "Example app spec")]
    [Description("A complete example app spec with options, branding, a module and two entities.")]
    public static string GetSpecExample() => SpecJson.Write(SpecJson.Example());

    [McpServerTool(Name = "validate_spec", ReadOnly = true, Title = "Validate an app spec")]
    [Description("Checks an app spec; returns ok or the list of problems, then a warn line for every option that is ignored because the option it depends on is off.")]
    public static string ValidateSpec(
        [Description("The app spec as JSON.")] string spec,
        [Description("Folder that relative logo and media files are resolved against; without it files are not checked.")] string? directory = null)
    {
        try
        {
            var app = SpecJson.Read(spec);
            var errors = SpecValidator.Validate(app, directory);
            return string.Join("\n", (errors.Count == 0 ? ["ok"] : errors).Concat(SpecValidator.Warnings(app).Select(w => $"warn  {w}")));
        }
        catch (System.Text.Json.JsonException exception)
        {
            return "The spec is not valid JSON: " + exception.Message;
        }
    }

    [McpServerTool(Name = "create_app", Destructive = false, OpenWorld = true, Title = "Create a Coworkee app")]
    [Description("Creates a Coworkee app with coworkee new: from a spec (see get_spec_schema), or from a name and options. Installs the template, generates the modules of the spec, restores and adds the needed migration. Takes minutes.")]
    public async Task<string> CreateAppAsync(
        [Description("Folder in which the app folder is created; relative logo and media files of the spec are resolved against it.")] string directory,
        [Description("The app spec as JSON. Leave out to use name and options.")] string? spec = null,
        [Description("Name of the app when there is no spec.")] string? name = null,
        [Description("Option values by id (list_options), e.g. {\"chat\": \"false\", \"theme\": \"Forest\"}, used when there is no spec.")] Dictionary<string, string>? options = null,
        [Description("Development users as " + UserSyntax.Format + ", added to those of the spec; each gets a generated password, the answer lists them. Roles with permissions go into the spec (roles).")] string[]? users = null,
        CancellationToken cancellationToken = default)
    {
        AppSpec app;
        try
        {
            app = spec is null ? new AppSpec { Name = name ?? string.Empty } : SpecJson.Read(spec);
        }
        catch (System.Text.Json.JsonException exception)
        {
            return "The spec is not valid JSON: " + exception.Message;
        }

        foreach (var (id, value) in options ?? [])
        {
            app.Options[id] = JsonValue.Create(value);
        }

        app.Users.AddRange((users ?? []).Select(UserSyntax.Parse));

        if (SpecValidator.Validate(app, directory) is { Count: > 0 } errors)
        {
            return string.Join("\n", errors);
        }

        // the spec sits in the folder, so its files stay relative to it and are checked again by coworkee new
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, $".coworkee-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(file, SpecJson.Write(app), cancellationToken);
        try
        {
            return (await run(CreateArgs(file, Path.Combine(directory, app.Output ?? app.Name)), directory, cancellationToken)).ToString();
        }
        finally
        {
            File.Delete(file);
        }
    }

    [McpServerTool(Name = "add_module", Title = "Add a feature module")]
    [Description("Adds a feature module with its first entity to the app in the folder: table, permissions, OData set, endpoints, validation and a Blazor page. Add a migration afterwards.")]
    public async Task<string> AddModuleAsync(
        [Description("A folder of the app (the .slnx is searched upwards).")] string directory,
        [Description("Module name, e.g. Sales.")] string module,
        [Description("Its first entity in singular (default: module name without trailing s).")] string? entity = null,
        [Description("Properties as " + FeatureOptions.PropertySyntax + ". Types: string, int, long, decimal, double, bool, dateTime, guid, dateOnly, timeOnly, text, email, url, money, enum, reference.")] string[]? properties = null,
        [Description("Mirror the OData set in the browser.")] bool clientEntity = false,
        [Description("Manual or Mapperly.")] string? mapping = null,
        [Description("Enums the properties use, as Name=Value1,Value2, e.g. OrderStatus=Open,Paid.")] string[]? enums = null,
        CancellationToken cancellationToken = default) =>
        (await run(["module", "add", module, .. entity is null ? [] : new[] { "--entity", entity }, .. Feature(properties, clientEntity, mapping, enums)], directory, cancellationToken)).ToString();

    [McpServerTool(Name = "add_entity", Title = "Add an entity")]
    [Description("Adds an entity to a module created with add_module: table, permissions, OData set, endpoints, validation and a Blazor page. Add a migration afterwards.")]
    public async Task<string> AddEntityAsync(
        [Description("A folder of the app.")] string directory,
        [Description("The module, e.g. Sales.")] string module,
        [Description("Entity name in singular, e.g. Invoice.")] string entity,
        [Description("Properties as " + FeatureOptions.PropertySyntax + "; references may point to entities of other modules (ref=Crm.Customer).")] string[]? properties = null,
        [Description("Plural for table, OData set and route (default: name + s).")] string? plural = null,
        [Description("Mirror the OData set in the browser.")] bool clientEntity = false,
        [Description("Manual or Mapperly.")] string? mapping = null,
        [Description("New enums the properties use, as Name=Value1,Value2.")] string[]? enums = null,
        CancellationToken cancellationToken = default) =>
        (await run(["entity", "add", module, entity, .. plural is null ? [] : new[] { "--plural", plural }, .. Feature(properties, clientEntity, mapping, enums)], directory, cancellationToken)).ToString();

    [McpServerTool(Name = "add_migration", Title = "Add a migration")]
    [Description("Adds an EF Core migration to the infrastructure project, e.g. after add_module or add_entity.")]
    public async Task<string> AddMigrationAsync([Description("A folder of the app.")] string directory, [Description("Migration name, e.g. AddSales.")] string name, CancellationToken cancellationToken = default) =>
        (await run(["migrations", "add", name], directory, cancellationToken)).ToString();

    [McpServerTool(Name = "add_option", OpenWorld = true, Title = "Turn options of an existing app on or off")]
    [Description("Turns options of an existing app on (or off with remove): modules like reports or chat, infrastructure like redis or keycloak, eventBus rabbitMq, languages fr,it, ai openAI. Merges the template's changes into the app's files (conflicts become .rej files, listed in the answer) and adds a migration when the model changed. Use dryRun first to see the plan.")]
    public async Task<string> AddOptionAsync(
        [Description("A folder of the app.")] string directory,
        [Description("Option ids, each optionally followed by its value, e.g. [\"reports\"], [\"eventBus\", \"rabbitMq\"] or [\"languages=fr,it\"] (list_options).")] string[] options,
        [Description("Turn the options off instead; skipped when a touched file conflicts with the app's changes.")] bool remove = false,
        [Description("Only print the plan.")] bool dryRun = false,
        CancellationToken cancellationToken = default) =>
        (await run([remove ? "remove" : "add", .. options, .. dryRun ? new[] { "--dry-run" } : []], directory, cancellationToken)).ToString();

    [McpServerTool(Name = "run_doctor", ReadOnly = true, Title = "Check the tools")]
    [Description("Checks the .NET SDK, Docker and dotnet-ef that a Coworkee app needs.")]
    public async Task<string> RunDoctorAsync([Description("Folder whose global.json sets the SDK (optional).")] string? directory = null, CancellationToken cancellationToken = default) =>
        (await run(["doctor"], directory ?? Environment.CurrentDirectory, cancellationToken)).ToString();

    [McpServerTool(Name = "update_packages", Title = "Update the Coworkee packages")]
    [Description("Sets the Coworkee package version of the app, by default the newest on nuget.org.")]
    public async Task<string> UpdatePackagesAsync(
        [Description("A folder of the app.")] string directory,
        [Description("Version to use.")] string? version = null,
        [Description("Allow prerelease versions when looking up the newest.")] bool prerelease = false,
        CancellationToken cancellationToken = default) =>
        (await run(["update", .. version is null ? [] : new[] { "--version", version }, .. prerelease ? new[] { "--prerelease" } : []], directory, cancellationToken)).ToString();

    [McpServerTool(Name = "search_docs", ReadOnly = true, OpenWorld = true, Title = "Search the Coworkee documentation")]
    [Description("Searches the Coworkee documentation and returns matching pages with address and snippet.")]
    public Task<string> SearchDocsAsync([Description("Words to search for, e.g. 'client entities' or 'sign-in design'.")] string query, CancellationToken cancellationToken = default) =>
        DocsSearch.SearchAsync(query, http, cancellationToken);

    public static string[] CreateArgs(string specFile, string output) => ["new", "--spec", specFile, "-o", output];

    private static string[] Feature(string[]? properties, bool clientEntity, string? mapping, string[]? enums) =>
    [
        .. (enums ?? []).SelectMany(e => new[] { "--enum", e }),
        .. (properties ?? []).SelectMany(p => new[] { "--property", p }),
        .. clientEntity ? new[] { "--client-entity" } : [],
        .. mapping is null ? [] : new[] { "--mapping", mapping },
    ];
}
