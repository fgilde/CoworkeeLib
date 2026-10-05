using System.Net.Http.Json;
using Coworkee.AspNetCore;
using Coworkee.Contracts.Identity;
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

[assembly: AssemblyFixture(typeof(Coworkee.OData.Tests.ODataApp))]
[assembly: CollectionBehavior(CollectionBehavior.CollectionPerAssembly)]

namespace Coworkee.OData.Tests;

public sealed class ODataApp : PostgresFixture
{
    public const string ViewGadgets = "Gadgets.View";

    public WebApplication App { get; private set; } = null!;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration["Coworkee:SetupToken"] = "token";
        builder.Configuration["ConnectionStrings:test"] = ConnectionString;
        builder.AddCoworkee<GadgetModule>();
        builder.Services.AddTestAuthentication();
        App = builder.Build();
        App.UseCoworkee();
        await using (var scope = App.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<GadgetDbContext>().Database.EnsureCreatedAsync();
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
        var response = await App.GetTestClient().PostAsJsonAsync("/api/v1/setup/complete",
            new CompleteSetupRequest("token", "Acme", "admin@acme.test", "Admin#12345", "Ada", "Admin"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SetupResultDto>())!;
    }

    public HttpClient As(Guid userId, Guid tenantId) => App.GetTestClient().AsUser(userId, tenantId);

    public async Task<T> InDbAsync<T>(Guid? tenantId, Func<GadgetDbContext, Task<T>> action)
    {
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(null, tenantId));
        await using var scope = App.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<GadgetDbContext>());
    }
}

public sealed class GadgetDbContext(DbContextOptions<GadgetDbContext> options, ICurrentUser currentUser, IEnumerable<IModelContributor> contributors)
    : CoworkeeDbContext(options, currentUser, contributors);

internal sealed class GadgetModelContributor : IModelContributor
{
    public void Apply(ModelBuilder modelBuilder) => modelBuilder.Entity<Gadget>(gadget => gadget.ToTable("Gadgets"));
}

internal sealed class GadgetPermissions : Coworkee.Application.Authorization.IPermissionDefinitionContributor
{
    public void Define(Coworkee.Application.Authorization.PermissionDefinitionContext context) =>
        context.Group("Gadgets", "Gadgets").Add(ODataApp.ViewGadgets, "View gadgets");
}

[DependsOn(typeof(CoworkeeODataModule), typeof(CoworkeeIdentityModule))]
public sealed class GadgetModule : CoworkeeModule
{
    public override void ConfigureServices(ModuleServiceContext context)
    {
        context.Services.AddSingleton<IModelContributor, GadgetModelContributor>();
        context.Services.AddSingleton<Coworkee.Application.Authorization.IPermissionDefinitionContributor, GadgetPermissions>();
        context.Services.AddCoworkeeDbContext<GadgetDbContext>((provider, options) =>
            options.UseNpgsql(provider.GetRequiredService<IConfiguration>().GetConnectionString("test")));
        context.Services.AddODataEntity<Gadget>("Gadgets", ODataApp.ViewGadgets, g => g.SerialCode);
        context.Services.AddScoped<IODataEntityFilter<Gadget>, HideSecretGadgets>();
    }
}

internal sealed class HideSecretGadgets : IODataEntityFilter<Gadget>
{
    public Task<IQueryable<Gadget>> ApplyAsync(IQueryable<Gadget> query, CancellationToken cancellationToken) =>
        Task.FromResult(query.Where(g => g.Category != "Secret"));
}
