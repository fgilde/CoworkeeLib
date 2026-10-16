namespace Coworkee.Cli.Spec;

/// <summary>The permissions an app of a spec defines, for the roles of the spec: the Coworkee modules its options include, the samples and the spec's entities.</summary>
public static class PermissionCatalog
{
    private static readonly (string? Option, string[] Permissions)[] Packages =
    [
        (null, [
            "Identity.Users.View", "Identity.Users.Manage", "Identity.Groups.View", "Identity.Groups.Manage", "Identity.Roles.View", "Identity.Roles.Manage",
            "Identity.Permissions.Manage", "Identity.ResourcePermissions.Manage", "Identity.Clients.Manage", "Settings.Manage", "Themes.Manage", "Localization.Manage",
            "Mail.Templates.Manage", "Mail.Log.View", "Jobs.View", "ApiDocs.View",
        ]),
        ("auditing", ["Audit.View"]),
        ("chat", ["Chat.Use"]),
        ("reports", ["Reports.View", "Reports.Manage", "Reports.Share", "Reports.Dashboard"]),
        ("ai", ["Ai.Chat", "Ai.Audit"]),
        ("backup", ["Backups.Manage"]),
        ("files", ["Files.View", "Files.Upload", "Files.Manage", "Files.Registrations.View"]),
        ("features", ["Features.Tenants", "Features.Editions"]),
        ("social", [
            "Social.Comments.View", "Social.Comments.Create", "Social.Comments.Moderate", "Social.Tags.View", "Social.Tags.Create", "Social.Tags.Moderate",
            "Social.Ratings.View", "Social.Ratings.Create", "Social.Ratings.Moderate",
        ]),
        ("workflows", ["Workflows.View", "Workflows.Transition"]),
        ("elsa", ["Workflows.Elsa.Manage"]),
        ("webhooks", ["Webhooks.Manage"]),
        ("samples", [
            "Catalog.Products.View", "Catalog.Products.Create", "Catalog.Products.Edit", "Catalog.Products.Delete", "Catalog.Products.Export",
            "Catalog.Brands.View", "Catalog.Brands.Create", "Catalog.Brands.Edit", "Catalog.Brands.Delete", "Catalog.Brands.Export", "Dashboards.View",
            "Documents.View", "Documents.Create", "Documents.Edit", "Documents.Delete", "Documents.Export", "Documents.ManageAll",
            "DocumentTypes.View", "DocumentTypes.Create", "DocumentTypes.Edit", "DocumentTypes.Delete",
        ]),
    ];

    private static readonly string[] EntityActions = ["View", "Create", "Edit", "Delete"];

    public static IReadOnlyList<string> For(AppSpec spec)
    {
        var values = SpecOptions.Resolve(spec);
        return
        [
            .. Packages.Where(p => p.Option is null || values[p.Option] == "true").SelectMany(p => p.Permissions),
            .. spec.Modules.SelectMany(m => m.Entities.SelectMany(e => EntityActions.Select(a => $"{m.Name}.{e.PluralName}.{a}"))),
        ];
    }

    /// <summary>The roles the template seeds itself.</summary>
    public static IReadOnlyList<string> BuiltInRoles(AppSpec spec) =>
        SpecOptions.Resolve(spec)["samples"] == "true" ? ["Product Manager", "Brand Manager"] : ["User"];
}
