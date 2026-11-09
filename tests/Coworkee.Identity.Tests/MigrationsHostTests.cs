using Coworkee.Application.Setup;
using Coworkee.Identity.Setup;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
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

    [Fact]
    public async Task The_steps_of_the_app_run_after_the_migration()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = "Production" });
        builder.Services.AddDbContext<BrokenDb>(o => o.UseSqlite($"Data Source={Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".db")}"));
        var cache = new RecordingCache();
        builder.Services.AddSingleton<HybridCache>(cache);
        var ran = false;
        builder.AddCoworkeeMigrations<BrokenDb>(o => o.AfterSeed = (services, _) =>
        {
            ran = services.GetService<BrokenDb>() is not null;
            return Task.CompletedTask;
        });
        using var host = builder.Build();

        (await host.RunCoworkeeMigrationsAsync(TestContext.Current.CancellationToken)).ShouldBe(0);
        ran.ShouldBeTrue();
        // running services drop what the run may have changed past their interceptors
        cache.Tags.ShouldBe(["coworkee:permissions", "coworkee:settings", "coworkee:features"]);
    }

    [Theory]
    [InlineData("Production", true, true)]
    [InlineData("Production", false, false)]
    [InlineData("Development", true, false)]
    public void Deployments_reconcile_the_seed_roles_only_when_asked(string environment, bool sync, bool registered)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = environment });
        builder.AddCoworkeeMigrations<BrokenDb>(o =>
        {
            o.DevelopmentSeed = seed => seed.Users.Add(new SeedUser("dev@seed.test", "Dev#12345", "Dev"));
            o.DeploymentSeed = (seed, admin) => seed.Roles.Add(new SeedRole("Dispatcher", null, []));
            o.SyncRolesInDeployments = sync;
        });

        builder.Services.Any(d => d.ServiceType == typeof(ISeedContributor)).ShouldBe(registered);
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

    private sealed class RecordingCache : HybridCache
    {
        public List<string> Tags { get; } = [];

        public override ValueTask<T> GetOrCreateAsync<TState, T>(
            string key, TState state, Func<TState, CancellationToken, ValueTask<T>> factory, HybridCacheEntryOptions? options = null, IEnumerable<string>? tags = null,
            CancellationToken cancellationToken = default) => factory(state, cancellationToken);

        public override ValueTask SetAsync<T>(string key, T value, HybridCacheEntryOptions? options = null, IEnumerable<string>? tags = null, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public override ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public override ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken = default)
        {
            Tags.Add(tag);
            return ValueTask.CompletedTask;
        }
    }
}
