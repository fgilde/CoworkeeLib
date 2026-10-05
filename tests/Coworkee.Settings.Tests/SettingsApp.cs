using System.Net.Http.Json;
using Coworkee.AspNetCore;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Settings;
using Coworkee.Core.Modularity;
using Coworkee.Core.Security;
using Coworkee.Identity;
using Coworkee.Identity.Setup;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

[assembly: AssemblyFixture(typeof(Coworkee.Settings.Tests.SettingsApp))]
[assembly: CollectionBehavior(CollectionBehavior.CollectionPerAssembly)]

namespace Coworkee.Settings.Tests;

public sealed class SettingsApp : PostgresFixture
{
    public WebApplication App { get; private set; } = null!;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration["Coworkee:SetupToken"] = "token";
        builder.Configuration["ConnectionStrings:test"] = ConnectionString;
        builder.Configuration["Coworkee:Settings:Defaults:Test.Configured"] = "7";
        builder.Configuration["TestApp:Name"] = "Default";
        builder.Configuration["TestApp:Limit"] = "10";
        builder.Configuration["TestApp:ApiKey"] = "secret-default";
        builder.Configuration["TestApp:Tags:0"] = "a";
        builder.Configuration["TestApp:Tags:1"] = "b";
        builder.Configuration.AddCoworkeeAppConfigurationDefaults(
            new MemoryStream("""{ "Name": "FromFile", "Mode": "file" /* comments are fine */ }"""u8.ToArray()), "TestApp");
        builder.Configuration.AddCoworkeeDatabaseConfiguration("test", TimeSpan.FromHours(1));
        builder.AddCoworkee<TestSettingsModule>();
        builder.Services.AddTestAuthentication();
        App = builder.Build();
        App.UseCoworkee();
        await using (var scope = App.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<SettingsTestDbContext>().Database.EnsureCreatedAsync();
        }

        await App.StartAsync();
    }

    public override async ValueTask DisposeAsync()
    {
        await App.DisposeAsync();
        await base.DisposeAsync();
    }

    public async Task<SetupResultDto> SetupAsync(IReadOnlyDictionary<string, string?>? settings = null)
    {
        await ResetAsync();
        App.Services.GetRequiredService<SystemStateCache>().Reset();
        var response = await App.GetTestClient().PostAsJsonAsync("/api/v1/setup/complete",
            new CompleteSetupRequest("token", "Acme", "admin@acme.test", "Admin#12345", "Ada", "Admin", settings));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SetupResultDto>())!;
    }

    /// <summary>The database provider after a reset or a write from outside the API.</summary>
    public void RefreshConfiguration() =>
        ((IConfigurationRoot)App.Services.GetRequiredService<IConfiguration>()).Providers.OfType<DatabaseConfigurationProvider>().Single().Refresh();

    public HttpClient As(Guid userId, Guid tenantId) => App.GetTestClient().AsUser(userId, tenantId);

    public async Task<T> InDbAsync<T>(Func<SettingsTestDbContext, Task<T>> action)
    {
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(null, null));
        await using var scope = App.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<SettingsTestDbContext>());
    }

    public Task<(Guid UserId, Guid TenantId)> CreateTenantAdminAsync() => InDbAsync(async db =>
    {
        var tenant = new Coworkee.Identity.Domain.Tenant { Name = "Other", Identifier = "other-" + Guid.NewGuid().ToString("N")[..8] };
        var email = $"admin-{Guid.NewGuid():N}@other.test";
        var user = new Coworkee.Identity.Domain.User { TenantId = tenant.Id, UserName = email, NormalizedUserName = email.ToUpperInvariant(), Email = email, NormalizedEmail = email.ToUpperInvariant() };
        var adminRole = await db.Set<Coworkee.Identity.Domain.Role>().Where(r => r.IsSystem && r.Name == Coworkee.Identity.Domain.SystemRoles.Admin).Select(r => r.Id).SingleAsync();
        db.Add(tenant);
        db.Add(user);
        db.Add(new Microsoft.AspNetCore.Identity.IdentityUserRole<Guid> { UserId = user.Id, RoleId = adminRole });
        await db.SaveChangesAsync();
        return (user.Id, tenant.Id);
    });

    public async Task<T> AsActorAsync<T>(Guid userId, Guid tenantId, Func<IServiceProvider, Task<T>> action)
    {
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(userId, tenantId));
        await using var scope = App.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }
}

public sealed class SettingsTestDbContext(DbContextOptions<SettingsTestDbContext> options, ICurrentUser currentUser, IEnumerable<IModelContributor> contributors)
    : CoworkeeDbContext(options, currentUser, contributors);

internal sealed class TestSettingDefinitions : ISettingDefinitionContributor
{
    public void Define(SettingDefinitionContext context) =>
        context.Group("Test", "Test")
            .Add("Test.Text", "Text", SettingType.String, [SettingScope.Global, SettingScope.Tenant, SettingScope.User], "fallback", visibleToClient: true)
            .Add("Test.GlobalOnly", "Global only", SettingType.Int, [SettingScope.Global], "5")
            .Add("Test.Configured", "Configured", SettingType.Int, [SettingScope.Global], "1")
            .Add("Test.Secret", "Secret", SettingType.Secret, [SettingScope.Global, SettingScope.Tenant], visibleToClient: true);
}

[DependsOn(typeof(CoworkeeSettingsModule), typeof(CoworkeeIdentityModule))]
public sealed class TestSettingsModule : CoworkeeModule
{
    public override void ConfigureServices(ModuleServiceContext context)
    {
        context.Services.AddSingleton<ISettingDefinitionContributor, TestSettingDefinitions>();
        context.Services.AddCoworkeeAppConfiguration<TestAppConfig>(context.Configuration, "TestApp", "Test app");
        context.Services.AddCoworkeeDbContext<SettingsTestDbContext>((provider, options) =>
            options.UseNpgsql(provider.GetRequiredService<IConfiguration>().GetConnectionString("test")));
    }
}

public sealed class TestAppConfig
{
    public string? Name { get; set; }

    public int Limit { get; set; }

    public string? ApiKey { get; set; }

    public List<string> Tags { get; set; } = [];

    public string? Mode { get; set; }
}
