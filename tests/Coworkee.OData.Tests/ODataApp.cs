using System.Net.Http.Json;
using Coworkee.Application.Messaging;
using Coworkee.AspNetCore;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Modularity;
using Coworkee.Core.Security;
using Coworkee.Identity;
using Coworkee.Identity.Setup;
using Coworkee.OData.GlobalSearch;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

[assembly: AssemblyFixture(typeof(Coworkee.OData.Tests.ODataApp))]
[assembly: CollectionBehavior(CollectionBehavior.CollectionPerAssembly)]

namespace Coworkee.OData.Tests;

public sealed class ODataApp : DatabaseFixture
{
    public const string ViewGadgets = "Gadgets.View";

    public const string SeeCategories = "Gadgets.Categories";

    public const string ViewBins = "Gadgets.Bins";

    public WebApplication App { get; private set; } = null!;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration["Coworkee:SetupToken"] = "token";
        builder.Configuration["ConnectionStrings:test"] = ConnectionString;
        builder.Configuration["Coworkee:Jobs:ConnectionStringName"] = "test";
        builder.Configuration["Coworkee:Jobs:RunServer"] = "false";
        builder.Configuration["Coworkee:OData:BackgroundExportRows"] = "5";
        builder.Configuration["Coworkee:Database:Provider"] = Provider;
        builder.AddCoworkee<GadgetModule>();
        builder.Services.AddTestAuthentication();
        builder.Services.AddSingleton(provider => new InlineBackgroundJobs.Root(provider));
        builder.Services.AddScoped<Coworkee.BackgroundJobs.IBackgroundJobs, InlineBackgroundJobs>();
        builder.Services.AddSingleton<FakeGroupScope>();
        builder.Services.AddSingleton<Coworkee.Application.Groups.IGroupScope>(provider => provider.GetRequiredService<FakeGroupScope>());
        App = builder.Build();
        await using (var scope = App.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<GadgetDbContext>().Database.EnsureCreatedAsync();
            await scope.ServiceProvider.GetRequiredService<ParcelDbContext>().GetService<IRelationalDatabaseCreator>().CreateTablesAsync();
        }

        App.UseCoworkee();
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
        App.Services.GetRequiredService<FakeGroupScope>().Stored = [];
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
    public void Apply(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Gadget>(gadget => gadget.ToTable("Gadgets"));
        modelBuilder.Entity<Widget>(widget => widget.ToTable("Widgets"));
        modelBuilder.Entity<Part>(part => part.ToTable("Parts"));
        modelBuilder.Entity<Crate>(crate => crate.ToTable("Crates"));
        modelBuilder.Entity<Shelf>(shelf =>
        {
            shelf.ToTable("Shelves");
            shelf.HasMany(s => s.Bins).WithOne().HasForeignKey(b => b.ShelfId);
            shelf.HasOne(s => s.Featured).WithMany().HasForeignKey(s => s.FeaturedId).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<Bin>(bin => bin.ToTable("Bins"));
        modelBuilder.Entity<BinLabel>(label => label.ToTable("BinLabels"));
        modelBuilder.Entity<Locker>(locker => locker.ToTable("Lockers"));
    }
}

internal sealed class GadgetPermissions : Coworkee.Application.Authorization.IPermissionDefinitionContributor
{
    public void Define(Coworkee.Application.Authorization.PermissionDefinitionContext context) =>
        context.Group("Gadgets", "Gadgets").Add(ODataApp.ViewGadgets, "View gadgets").Add(ODataApp.SeeCategories, "See categories").Add(ODataApp.ViewBins, "View bins");
}

[DependsOn(typeof(CoworkeeODataModule), typeof(CoworkeeIdentityModule))]
public sealed class GadgetModule : CoworkeeModule
{
    public override void ConfigureServices(ModuleServiceContext context)
    {
        context.Services.AddSingleton<IModelContributor, GadgetModelContributor>();
        context.Services.AddSingleton<Coworkee.Application.Authorization.IPermissionDefinitionContributor, GadgetPermissions>();
        context.Services.AddCoworkeeDbContext<GadgetDbContext>((provider, options) =>
            options.UseCoworkeeDatabase(provider.GetRequiredService<IConfiguration>(), "test"));
        context.Services.AddODataEntity<Gadget>("Gadgets", ODataApp.ViewGadgets, o => o.Hide(g => g.SerialCode).HideUnless(g => g.Category, ODataApp.SeeCategories));
        context.Services.AddScoped<IODataEntityFilter<Gadget>, HideSecretGadgets>();
        context.Services.AddGlobalSearch<Gadget>(s => s.Fields(g => g.Name, g => g.Category).Subtitle(g => g.Category).Page("/gadgets"));
        context.Services.AddODataEntity<Widget>("Widgets", ODataApp.ViewGadgets);
        context.Services.ConfigureFacet<Widget>(w => w.Color, f => f.View = Contracts.Data.FacetView.Buttons);
        context.Services.AddMessagingFromAssembly(typeof(GadgetModule).Assembly);
        context.Services.AddODataImport<AddGadgetCommand>("Gadgets");
        context.Services.AddODataEntity<Part>("Parts", ODataApp.ViewGadgets);
        context.Services.AddScoped<IODataEntityFilter<Widget>, HideSecretWidgets>();
        context.Services.AddODataChildFilter<Part, Widget>(p => p.Widget);
        context.Services.AddODataImport("Widgets", (Guid? id, WidgetRow row) => new SaveWidgetCommand(id, row), id => new DeleteWidgetCommand(id));
        context.Services.AddODataImport<AddPartCommand>("Parts");
        context.Services.AddODataEntity<Crate>("Crates", ODataApp.ViewGadgets);
        context.Services.AddScoped<IODataEntityFilter<Crate>, CratesOfActiveUnits>();
        context.Services.AddGlobalSearch<Crate>(s => s.Fields(c => c.Name).Page("/crates"));
        context.Services.AddODataImport("Crates", (Guid? id, CrateRow row) => new SaveCrateCommand(id, row));
        context.Services.AddODataEntity<Shelf>("Shelves", ODataApp.ViewGadgets);
        context.Services.AddODataEntity<Bin>("Bins", ODataApp.ViewBins, o => o.HideUnless(b => b.Size, ODataApp.SeeCategories));
        context.Services.AddScoped<IODataEntityFilter<Bin>, HideSecretBins>();
        context.Services.AddScoped<IODataEntityFilter<BinLabel>, HideSecretLabels>();
        context.Services.AddODataEntity<Locker>("Lockers", ODataApp.ViewGadgets);
        context.Services.AddScoped<IODataEntityFilter<Locker>, FirstLockers>();
        context.Services.AddDbContext<ParcelDbContext>((provider, options) =>
            options.UseCoworkeeDatabase(provider.GetRequiredService<IConfiguration>(), "test"));
        context.Services.AddCoworkeeExternalDbContext<ParcelDbContext>();
        context.Services.AddODataEntity<Parcel>("Parcels", ODataApp.ViewGadgets);
        context.Services.AddScoped<IODataEntityFilter<Parcel>, HideSecretParcels>();
        context.Services.AddGlobalSearch<Parcel>(s => s.Fields(p => p.Name).Page("/parcels"));
        context.Services.Configure<ClientEntityOptions>(o => o.For<Parcel>(s => s.ChangedAt = [nameof(Parcel.DateCreatedUtc), nameof(Parcel.DateUpdatedUtc)]));
    }
}

internal sealed class HideSecretWidgets : IODataEntityFilter<Widget>
{
    public Task<IQueryable<Widget>> ApplyAsync(IQueryable<Widget> query, CancellationToken cancellationToken) =>
        Task.FromResult(query.Where(w => w.Color != "Secret"));
}

internal sealed class HideSecretGadgets : IODataEntityFilter<Gadget>
{
    public Task<IQueryable<Gadget>> ApplyAsync(IQueryable<Gadget> query, CancellationToken cancellationToken) =>
        Task.FromResult(query.Where(g => g.Category != "Secret"));
}
