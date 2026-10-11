using Coworkee.Cli.Commands;
using Coworkee.Cli.Generation;
using Coworkee.Cli.Spec;

namespace Coworkee.Cli.Tests;

public sealed class UserTests(TemplateFixture template)
{
    [Fact]
    public void Users_and_roles_are_checked_against_the_roles_and_permissions_of_the_app()
    {
        var spec = new AppSpec
        {
            Name = "Shop",
            Options = { ["samples"] = false },
            Roles =
            [
                new RoleSpec { Name = "Clerk", Permissions = ["Identity.Users.View", "Catalog.Products.View"] },
                new RoleSpec { Name = "User" },
                new RoleSpec { Name = "Bad\"Role" },
            ],
            Users =
            [
                new UserSpec { Email = "ada@shop.test", Roles = ["Clerk", "User"] },
                new UserSpec { Email = "admin@shop.local" },
                new UserSpec { Email = "no-address", Roles = ["Ghost"] },
                new UserSpec { Email = "bob@shop.test", Password = "short" },
                new UserSpec { Email = "ADA@shop.test", DisplayName = "<b>" },
            ],
        };

        var errors = SpecValidator.Validate(spec);

        errors.ShouldContain(e => e.StartsWith("roles[0].permissions: 'Catalog.Products.View' is not a permission of the app", StringComparison.Ordinal));
        errors.ShouldContain("roles[1].name: 'User' exists already.");
        errors.ShouldContain(e => e.StartsWith("roles[2].name:", StringComparison.Ordinal));
        errors.ShouldContain("users[1].email: 'admin@shop.local' exists already.");
        errors.ShouldContain("users[2].email: 'no-address' is not an email address.");
        errors.ShouldContain(e => e.StartsWith("users[2].roles: 'Ghost' is no role", StringComparison.Ordinal));
        errors.ShouldContain(e => e.StartsWith("users[3].password:", StringComparison.Ordinal));
        errors.ShouldContain("users[4].email: 'ADA@shop.test' exists already.");
        errors.ShouldContain(e => e.StartsWith("users[4].displayName:", StringComparison.Ordinal));
        errors.Count.ShouldBe(9);
        PermissionCatalog.For(SpecJson.Example()).ShouldContain("Sales.Orders.Edit");
        PermissionCatalog.For(new AppSpec { Name = "Shop" }).ShouldContain("Catalog.Products.View");
    }

    [Theory]
    [InlineData("ada@shop.test", false, new string[0])]
    [InlineData("ada@shop.test:admin", true, new string[0])]
    [InlineData("ada@shop.test:Product Manager,Brand Manager:admin", true, new[] { "Product Manager", "Brand Manager" })]
    public void The_user_switch_reads_email_roles_and_admin(string text, bool admin, string[] roles)
    {
        var settings = NewCommand.Read(CliApp.Build().Parse(["new", "Shop", "--user", text, "--user", "bob@shop.test"]));

        settings.Spec.Users.Select(u => u.Email).ShouldBe(["ada@shop.test", "bob@shop.test"]);
        settings.Spec.Users[0].Admin.ShouldBe(admin);
        settings.Spec.Users[0].Roles.ShouldBe(roles);
    }

    [Fact]
    public void Names_come_from_the_display_name_or_the_email()
    {
        DemoUsersWriter.Names(new UserSpec { Email = "ada.lovelace@shop.test" }).ShouldBe(("Ada", "Lovelace"));
        DemoUsersWriter.Names(new UserSpec { Email = "x@shop.test", DisplayName = "Grace Brewster Hopper" }).ShouldBe(("Grace", "Brewster Hopper"));
        DemoUsersWriter.Names(new UserSpec { Email = "bob@shop.test" }).ShouldBe(("Bob", null));
        SpecValidator.IsPassword(DemoUsersWriter.NewPassword()).ShouldBeTrue();
    }

    [Fact]
    public async Task Spec_users_and_roles_reach_demo_users_login_and_the_sign_in_box()
    {
        var app = await template.CreateAsync("users", "--samples", "false", "--keycloak", "true");
        var spec = SpecJson.Example();

        DemoUsersWriter.Write(Solution.Find(app), spec);

        var info = SignInInfo.Read(app).ShouldNotBeNull();
        info.Keycloak.ShouldBeTrue();
        info.WebResource.ShouldBe("shop-web");
        info.Users.Select(u => u.Email).ShouldBe(["admin@shop.local", "ada@shop.test", "grace@shop.test"]);
        info.Users[0].IsAdmin.ShouldBeTrue();
        info.Users[0].Password.ShouldMatch("^Cw1!(?i:[0-9a-f]{32})$");
        info.Users[1].Roles.ShouldBe(["Sales"]);
        info.Users[1].Password.ShouldMatch("^Cw1!(?i:[0-9a-f]{32})$");
        info.Users[2].Password.ShouldBe("Grace-2026!");
        var users = File.ReadAllText(Path.Combine(app, "src", "Shop.Migrations", "DemoUsers.cs"));
        users.ShouldContain("new(\"Sales\", \"Takes orders\", [\"Sales.Customers.View\", \"Sales.Orders.View\", \"Sales.Orders.Create\", \"Sales.Orders.Edit\"]),");
        users.ShouldContain("new(\"ada@shop.test\", \"Ada\", \"Lovelace\", ");
        users.ShouldContain("new(\"grace@shop.test\", \"Grace\", null, \"Grace-2026!\", IsAdmin: false, Roles: [\"User\"]),");
        var login = File.ReadAllText(Path.Combine(app, "LOGIN.md"));
        login.ShouldContain("| `grace@shop.test` | `Grace-2026!` | User |");
        login.ShouldContain($"| `ada@shop.test` | `{info.Users[1].Password}` | Sales |");
        login.ShouldContain("Keycloak");
        var box = SignInBox.Lines(info);
        box[0].ShouldStartWith("+-- Sign in ");
        box.ShouldContain(l => l.Contains("grace@shop.test", StringComparison.Ordinal) && l.Contains("Grace-2026!", StringComparison.Ordinal));
        box.ShouldContain(l => l.Contains("Keycloak button", StringComparison.Ordinal));
        box.Select(l => l.Length).Distinct().Count().ShouldBe(1);
        File.ReadAllText(Path.Combine(app, "src", "Shop.AppHost", "AppHost.cs")).ShouldContain("DemoUsers.All.Select(u => new KeycloakUser(");
    }

    [Fact]
    public async Task Without_keycloak_and_registration_the_login_file_says_neither()
    {
        var app = await template.CreateAsync("plain-login", "--samples", "false", "--registration", "false");

        var login = File.ReadAllText(Path.Combine(app, "LOGIN.md"));
        login.ShouldNotContain("Keycloak");
        login.ShouldNotContain("create an account");
        SignInInfo.Read(app)!.Keycloak.ShouldBeFalse();
        TemplateTests.ShouldHaveNoLeftovers(app);
    }

    [Fact]
    public async Task Generated_app_with_keycloak_users_and_roles_builds_against_the_local_feed()
    {
        var feed = Environment.GetEnvironmentVariable("COWORKEE_TEST_FEED");
        Assert.SkipWhen(string.IsNullOrEmpty(feed), "Set COWORKEE_TEST_FEED to a folder with packed Coworkee packages (build/pack-local.ps1) to build a generated app.");
        var app = await template.CreateAsync("users-build", "--samples", "false", "--keycloak", "true");
        DemoUsersWriter.Write(Solution.Find(app), new AppSpec
        {
            Name = "Shop",
            Roles = [new RoleSpec { Name = "Auditor", Description = "Reads users", Permissions = ["Identity.Users.View"] }],
            Users = [new UserSpec { Email = "ada@shop.test", Roles = ["Auditor", "User"] }, new UserSpec { Email = "root@shop.test", Admin = true }],
        });
        await Dotnet.RunAsync(app, "nuget", "add", "source", Path.GetFullPath(feed!), "--name", "local", "--configfile", "nuget.config");

        await Dotnet.RunAsync(app, "build", "Shop.slnx");
    }
}
