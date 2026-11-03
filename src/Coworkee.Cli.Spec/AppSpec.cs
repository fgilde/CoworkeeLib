using System.ComponentModel;
using System.Text.Json.Nodes;

namespace Coworkee.Cli.Spec;

/// <summary>Everything <c>coworkee new --spec app.json</c> needs to create an application.</summary>
public sealed class AppSpec
{
    [Description("Name of the app: a letter, then letters or digits. Prefix of every project and namespace.")]
    public string Name { get; set; } = string.Empty;

    [Description("Target folder, relative to the spec file (default: ./<name> next to it).")]
    public string? Output { get; set; }

    [Description("Template options by id (coworkee options), e.g. { \"chat\": false, \"eventBus\": \"rabbitMq\" }.")]
    public Dictionary<string, JsonNode?> Options { get; set; } = [];

    [Description("Title, theme, colors, logo and sign-in design; the same ids as in options, kept apart for readability.")]
    public BrandingSpec? Branding { get; set; }

    [Description("Modules of your own with their entities (generated code). The modules of the library (chat, files, ...) are options; a spec lists the ones that differ from the defaults.")]
    public List<ModuleSpec> Modules { get; set; } = [];

    [Description("Enums properties of type enum use; generated into the contracts project.")]
    public List<EnumSpec> Enums { get; set; } = [];

    [Description("Roles seeded with the app, besides the built-in ones (with samples: Product Manager, Brand Manager; without: User).")]
    public List<RoleSpec> Roles { get; set; } = [];

    [Description("Development users seeded with the app (and into the Keycloak realm with the same password), besides the administrator admin@<name>.local.")]
    public List<UserSpec> Users { get; set; } = [];
}
