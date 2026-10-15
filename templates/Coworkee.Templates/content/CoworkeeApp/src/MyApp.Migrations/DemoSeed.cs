#if (reports)
using Coworkee.Contracts.Reporting;
#endif
#if (chat)
using Coworkee.Contracts.Chat;
#endif
#if (workflows)
using Coworkee.Contracts.Workflows;
#endif
using Coworkee.Identity.Setup;
#if (samples)
using MyApp.Contracts.Catalog;
using MyApp.Contracts.Documents;
#endif

namespace MyApp.Migrations;

/// <summary>Development seed with the users and roles of <see cref="DemoUsers"/>; it runs in the Development environment only.</summary>
public static class DemoSeed
{
    public static void Configure(IdentitySeedOptions seed)
    {
        seed.TenantName = "COWORKEE_APP_TITLE";
#if (samples)
        seed.Roles.Add(new SeedRole("Product Manager", "Maintains products", [.. Shared, CatalogPermissions.Products.View, CatalogPermissions.Products.Create, CatalogPermissions.Products.Edit, CatalogPermissions.Products.Delete, CatalogPermissions.Brands.View]));
        seed.Roles.Add(new SeedRole("Brand Manager", "Maintains brands", [.. Shared, CatalogPermissions.Brands.View, CatalogPermissions.Brands.Create, CatalogPermissions.Brands.Edit, CatalogPermissions.Brands.Delete]));
#else
        seed.Roles.Add(new SeedRole("User", "Signed-in users", Shared));
#endif
        seed.Roles.AddRange(DemoUsers.Roles.Select(r => new SeedRole(r.Name, r.Description, [.. Shared.Union(r.Permissions)])));
        seed.Users.AddRange(DemoUsers.All.Select(u => new SeedUser(u.Email, u.Password, u.FirstName, u.LastName, u.IsAdmin) { Roles = u.Roles ?? [] }));
#if (colorMode != "system")
        seed.Settings["Theme.Mode"] = "COWORKEE_COLOR_MODE";
#endif
#if (registration)
        seed.Settings["Account.AllowRegistration"] = "true";
#endif
    }

    private static string[] Shared =>
    [
#if (chat)
        ChatPermissions.Use,
#endif
#if (reports)
        ReportPermissions.View,
#endif
#if (workflows)
        WorkflowPermissions.View, WorkflowPermissions.Transition,
#endif
#if (samples)
        CatalogPermissions.Dashboards.View,
        DocumentPermissions.Documents.View, DocumentPermissions.Documents.Create, DocumentPermissions.Documents.Edit, DocumentPermissions.Documents.Delete,
#endif
    ];
}
