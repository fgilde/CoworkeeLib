using Coworkee.Cli.Spec;
using Coworkee.Studio.Cli;

namespace Coworkee.Studio.Tests;

public sealed class UsersTests
{
    [Fact]
    public Task Users_and_roles_reach_the_spec_offer_the_permissions_of_the_app_and_survive_a_reload() => TestApp.Ui(() =>
    {
        using var folder = new TempFolder();
        var main = Scenario.Shop(folder);

        var spec = main.ToSpec();

        spec.Roles.Single().ShouldSatisfyAllConditions(
            r => r.Name.ShouldBe("Sales"),
            r => r.Permissions.ShouldContain("Sales.Orders.Edit"),
            r => r.Permissions.ShouldNotContain("Sales.Orders.Delete"));
        spec.Users.Select(u => u.Email).ShouldBe(["ada@shop.test", "grace@shop.test"]);
        spec.Users[0].Roles.ShouldBe(["Sales"]);
        spec.Users[1].Admin.ShouldBeTrue();
        main.Users.Roles[0].Permissions.Select(p => p.Name).ShouldContain("Audit.View");
        main.Users.Roles[0].Permissions.Select(p => p.Name).ShouldNotContain("Chat.Use");
        main.HasErrors.ShouldBeFalse();
        CommandLine.Flags(spec).ShouldContain("ada@shop.test");
        CommandLine.Flags(spec).ShouldContain("grace@shop.test:User:admin");

        var reloaded = Studio.Create(folder);
        reloaded.Load(SpecJson.Read(SpecJson.Write(spec)), null);
        SpecJson.Write(reloaded.ToSpec()).ShouldBe(SpecJson.Write(main.ToSpec()));
    });

    [Fact]
    public Task Problems_of_users_show_at_the_user_and_on_the_step() => TestApp.Ui(() =>
    {
        using var folder = new TempFolder();
        var main = Scenario.Shop(folder);

        main.Users.Users[0].Email = "not an address";
        main.Users.Users[1].Password = "weak";
        main.Users.Roles[0].Name = "User";

        main.Users.Users[0].Errors.ShouldContain("users[0].email: 'not an address' is not an email address.");
        main.Users.Users[0].Errors.ShouldContain(e => e.StartsWith("users[0].roles: 'Sales'", StringComparison.Ordinal));
        main.Users.Users[1].Errors.ShouldHaveSingleItem().ShouldStartWith("users[1].password");
        main.Users.Roles[0].Errors.ShouldContain("roles[0].name: 'User' exists already.");
        main.Users.HasErrors.ShouldBeTrue();
        main.Entities.HasErrors.ShouldBeFalse();
    });

    [Fact]
    public Task A_created_app_shows_its_users_with_passwords() => TestApp.Ui(() =>
    {
        using var folder = new TempFolder();
        var main = Scenario.Shop(folder);
        var output = Directory.CreateDirectory(folder.File("Shop")).FullName;
        Directory.CreateDirectory(Path.Combine(output, "src", "Shop.Migrations"));
        Directory.CreateDirectory(Path.Combine(output, "src", "Shop.AppHost"));
        File.WriteAllText(Path.Combine(output, "src", "Shop.Migrations", "DemoUsers.cs"), """
            new("admin@shop.local", "Administrator", null, "Cw1!0123456789abcdef0123456789abcdef", IsAdmin: true),
            new("ada@shop.test", "Ada", "Lovelace", "Ada-Pass1!", IsAdmin: false, Roles: ["Sales", "User"]),
            """);
        File.WriteAllText(Path.Combine(output, "src", "Shop.AppHost", "AppHost.cs"), "options.UseGrafana(); options.UseWebDataStudio(); options.UseKeycloak(k => { });");
        main.Generate.Output = output;

        main.Generate.Finish(GenerationOutcome.Succeeded);

        main.Generate.HasSignIn.ShouldBeTrue();
        main.Generate.SignIn!.Keycloak.ShouldBeTrue();
        main.Generate.SignIn.Users[1].RoleText.ShouldBe("Sales, User");
        main.Generate.SignInHint.ShouldContain("shop-web");
        main.Generate.SignInText.ShouldContain("ada@shop.test\tAda-Pass1!\tSales, User");
    });
}
