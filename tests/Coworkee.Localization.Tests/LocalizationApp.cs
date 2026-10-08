using System.Net.Http.Json;
using Coworkee.AspNetCore;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Modularity;
using Coworkee.Identity;
using Coworkee.Identity.Setup;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Localization.Resources;
using Coworkee.Localization.Texts;
using Coworkee.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

[assembly: AssemblyFixture(typeof(Coworkee.Localization.Tests.LocalizationApp))]
[assembly: CollectionBehavior(CollectionBehavior.CollectionPerAssembly)]

namespace Coworkee.Localization.Tests;

public sealed class LocalizationApp : PostgresFixture
{
    public WebApplication App { get; private set; } = null!;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration["Coworkee:SetupToken"] = "token";
        builder.Configuration["ConnectionStrings:test"] = ConnectionString;
        builder.AddCoworkee<TestLocalizationModule>();
        builder.Services.AddTestAuthentication();
        App = builder.Build();
        App.UseCoworkee();
        await using (var scope = App.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<LocalizationTestDbContext>().Database.EnsureCreatedAsync();
        }

        await App.StartAsync();
    }

    public override async ValueTask DisposeAsync()
    {
        await App.DisposeAsync();
        await base.DisposeAsync();
    }

    public async Task<SetupResultDto> SetupAsync()
    {
        await ResetAsync();
        App.Services.GetRequiredService<SystemStateCache>().Reset();
        await App.Services.GetRequiredService<HybridCache>().RemoveByTagAsync(TextStore.CacheTag);
        var response = await App.GetTestClient().PostAsJsonAsync("/api/v1/setup/complete",
            new CompleteSetupRequest("token", "Acme", "admin@acme.test", "Admin#12345", "Ada", "Admin"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SetupResultDto>())!;
    }

    public HttpClient Anonymous() => App.GetTestClient();

    public HttpClient As(Guid userId, Guid tenantId) => App.GetTestClient().AsUser(userId, tenantId);
}

public sealed class LocalizationTestDbContext(
    DbContextOptions<LocalizationTestDbContext> options, Coworkee.Core.Security.ICurrentUser currentUser, IEnumerable<IModelContributor> contributors)
    : CoworkeeDbContext(options, currentUser, contributors);

internal sealed class TestTexts : ILocalizationResourceContributor
{
    public void Define(LocalizationResourceContext context) =>
        context.Add("de", new Dictionary<string, string> { ["Brands"] = "Marken", ["Save"] = "Speichern" })
            .AddEmbeddedJson(typeof(TestTexts).Assembly);
}

[DependsOn(typeof(CoworkeeLocalizationModule), typeof(CoworkeeIdentityModule))]
public sealed class TestLocalizationModule : CoworkeeModule
{
    public override void ConfigureServices(ModuleServiceContext context)
    {
        context.Services.AddSingleton<ILocalizationResourceContributor, TestTexts>();
        context.Services.AddHttpClient(Coworkee.Localization.MachineTranslation.TextTranslator.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => new StubTranslator());
        context.Services.AddCoworkeeDbContext<LocalizationTestDbContext>((provider, options) =>
            options.UseNpgsql(provider.GetRequiredService<IConfiguration>().GetConnectionString("test")));
    }
}
