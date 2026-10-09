using Coworkee.Contracts.Chat;
using Coworkee.Identity.Setup;
#if (samples)
using MyApp.Contracts.Catalog;
using MyApp.Contracts.Documents;
#endif

namespace MyApp.Migrations;

/// <summary>Development seed. The password was generated when the solution was created; change it before you deploy anywhere.</summary>
public static class DemoSeed
{
    public static readonly SeedUser Administrator = new("admin@myapp.local", "Cw1!SEED_PASSWORD", "Administrator", null, IsAdmin: true);

    public static void Configure(IdentitySeedOptions seed)
    {
        seed.TenantName = "COWORKEE_APP_TITLE";
#if (samples)
        seed.Roles.Add(new SeedRole("Product Manager", "Maintains products", [.. Shared, CatalogPermissions.Products.View, CatalogPermissions.Products.Create, CatalogPermissions.Products.Edit, CatalogPermissions.Products.Delete, CatalogPermissions.Brands.View]));
        seed.Roles.Add(new SeedRole("Brand Manager", "Maintains brands", [.. Shared, CatalogPermissions.Brands.View, CatalogPermissions.Brands.Create, CatalogPermissions.Brands.Edit, CatalogPermissions.Brands.Delete]));
#else
        seed.Roles.Add(new SeedRole("User", "Signed-in users", Shared));
#endif
        seed.Users.Add(Administrator);
    }

    private static string[] Shared =>
    [
        ChatPermissions.Use,
#if (samples)
        CatalogPermissions.Dashboards.View,
        DocumentPermissions.Documents.View, DocumentPermissions.Documents.Create, DocumentPermissions.Documents.Edit, DocumentPermissions.Documents.Delete,
#endif
    ];
}
