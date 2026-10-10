using Coworkee.Studio.Localization;

namespace Coworkee.Studio.Views;

public sealed record McpTool(string Name, string Description)
{
    public string Text => L.Get(Description);
}

public sealed record CliCommandHelp(string Command, string Description)
{
    public string Text => L.Get(Description);
}

/// <summary>Texts of the help drawer that are copied as they are.</summary>
public static class HelpSnippets
{
    public const string Install = "dotnet tool install -g Coworkee.Cli";
    public const string ClaudeCode = "claude mcp add coworkee -- coworkee mcp";
    public const string ClaudeDesktop = """
        {
          "mcpServers": {
            "coworkee": { "command": "coworkee", "args": ["mcp"] }
          }
        }
        """;
    public const string VsCode = """
        {
          "servers": {
            "coworkee": { "type": "stdio", "command": "coworkee", "args": ["mcp"] }
          }
        }
        """;
    public const string Prompt = @"Create a shop app in C:\work: no samples, no chat, with files and the Forest theme in our green #0a7d5a. It needs customers (name, email) and orders (number, total, order date) that users filter all day.";
    public const string Docs = "https://fgilde.github.io/CoworkeeLib/";
    public const string McpDocs = "https://fgilde.github.io/CoworkeeLib/getting-started/mcp/";
    public const string CliDocs = "https://fgilde.github.io/CoworkeeLib/getting-started/cli/";
    public const string StudioDocs = "https://fgilde.github.io/CoworkeeLib/getting-started/studio/";

    public static IReadOnlyList<McpTool> Tools { get; } =
    [
        new("list_options", "Every option of a new app with id, kind, default, choices, pros, cons and when to use it."),
        new("get_spec_schema", "JSON Schema of the app spec: name, options, branding, modules with entities."),
        new("get_spec_example", "A complete example spec with options, branding, a module and two entities."),
        new("validate_spec", "Checks a spec; with a folder also its logo and media files."),
        new("create_app", "coworkee new from a spec, or from a name and options, in a folder. Takes minutes."),
        new("add_module", "A feature module with its first entity: table, permissions, OData set, endpoints, page."),
        new("add_entity", "Another entity in a module created with add_module."),
        new("add_migration", "An EF Core migration, e.g. after add_module or add_entity."),
        new("run_doctor", "Checks the .NET SDK, Docker and dotnet-ef."),
        new("update_packages", "Sets the Coworkee package version of the app."),
        new("search_docs", "Searches the Coworkee documentation."),
    ];

    public static IReadOnlyList<CliCommandHelp> Commands { get; } =
    [
        new("coworkee new Shop", "Create an app from switches; coworkee new --spec app.json from a spec."),
        new("coworkee options", "Every switch of coworkee new with pros, cons and when to use it."),
        new("coworkee spec example | schema | validate", "Example spec, its JSON Schema, and the check the Studio runs."),
        new("coworkee run", "Start the Aspire app host with every service and container."),
        new("coworkee migrations add Name", "Add an EF Core migration to the app."),
        new("coworkee module add Sales --entity Order", "Add a feature module with its first entity."),
        new("coworkee entity add Sales Invoice", "Add an entity to a module."),
        new("coworkee update", "Move the app to the newest Coworkee packages."),
        new("coworkee doctor", "Check SDK, Docker and dotnet-ef."),
        new("coworkee ui", "Install or update and start this Studio."),
        new("coworkee mcp", "MCP server over stdio for AI agents."),
    ];
}
