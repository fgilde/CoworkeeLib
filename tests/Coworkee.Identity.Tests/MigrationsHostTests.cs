using Coworkee.Identity.Setup;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Coworkee.Identity.Tests;

public sealed class MigrationsHostTests
{
    [Theory]
    [InlineData("Development", null, new[] { "dev@seed.test" })]
    [InlineData("Production", null, new string[0])]
    [InlineData("Production", true, new[] { "dev@seed.test" })]
    [InlineData("Development", false, new string[0])]
    public void A_run_without_administrator_seeds_the_development_users_where_wanted(string environment, bool? developmentUsers, string[] users) =>
        SeededUsers(environment, [], o => o.DevelopmentUsers = developmentUsers).ShouldBe(users);

    [Fact]
    public void A_deployment_seeds_its_first_administrator_instead_of_the_development_users() =>
        SeededUsers("Development", new() { ["Coworkee:InitialAdmin:Email"] = "admin@deploy.test", ["Coworkee:InitialAdmin:Password"] = "Deploy#12345" })
            .ShouldBe(["admin@deploy.test", "extra@deploy.test"]);

    [Fact]
    public async Task An_unreachable_database_ends_with_exit_code_1()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = "Production" });
        builder.Configuration["Migrations:Attempts"] = "1";
        builder.Services.AddDbContext<BrokenDb>(o => o.UseSqlite($"Data Source={Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "none", "x.db")};Mode=ReadWrite"));
        builder.AddCoworkeeMigrations<BrokenDb>();
        using var host = builder.Build();

        (await host.RunCoworkeeMigrationsAsync(TestContext.Current.CancellationToken)).ShouldBe(1);
    }

    private static string[] SeededUsers(string environment, Dictionary<string, string?> settings, Action<CoworkeeMigrationsOptions>? configure = null)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = environment });
        builder.Configuration.AddInMemoryCollection(settings);
        builder.AddCoworkeeMigrations<BrokenDb>(o =>
        {
            o.DevelopmentSeed = seed => seed.Users.Add(new SeedUser("dev@seed.test", "Dev#12345", "Dev"));
            o.DeploymentSeed = (seed, admin) => seed.Users.AddRange([admin, new SeedUser("extra@deploy.test", "Extra#12345", "Extra")]);
            configure?.Invoke(o);
        });
        using var services = builder.Services.BuildServiceProvider();
        return [.. services.GetRequiredService<IOptions<IdentitySeedOptions>>().Value.Users.Select(u => u.Email)];
    }

    private sealed class BrokenDb(DbContextOptions<BrokenDb> options) : DbContext(options);
}
