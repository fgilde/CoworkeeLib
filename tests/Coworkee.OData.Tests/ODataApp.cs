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
        App = Build();
        await using (var scope = App.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<GadgetDbContext>().Database.EnsureCreatedAsync();
            await scope.ServiceProvider.GetRequiredService<ParcelDbContext>().GetService<IRelationalDatabaseCreator>().CreateTablesAsync();
        }

        App.UseCoworkee();
        await App.StartAsync();
    }

    /// <summary>A second app on the same database with other settings, e.g. options the shared app keeps for all tests.</summary>
    public async Task<WebApplication> StartAnotherAsync(IReadOnlyDictionary<string, string> settings)
    {
        var app = Build(settings);
        app.UseCoworkee();
        await app.StartAsync();
        return app;
    }

    private WebApplication Build(IReadOnlyDictionary<string, string>? settings = null)
    {
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
        foreach (var (key, value) in settings ?? new Dictionary<string, string>())
        {
            builder.Configuration[key] = value;
        }

        return builder.Build();
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
        modelBuilder.Entity<Badge>(badge => badge.ToTable("Badges"));
        modelBuilder.Entity<Tray>(tray => tray.ToTable("Trays"));
        modelBuilder.Entity<Peg>(peg => peg.ToTable("Pegs"));
        modelBuilder.Entity<Coupon>(coupon => coupon.ToTable("Coupons").HasKey(c => c.Code));
        modelBuilder.Entity<Brand>(brand => brand.ToTable("Brands"));
        modelBuilder.Entity<Hook>(hook =>
        {
            hook.ToTable("Hooks");
            hook.HasOne(h => h.Spare).WithMany().HasForeignKey("SpareId").OnDelete(DeleteBehavior.NoAction);
            hook.HasOne(h => h.Main).WithMany().HasForeignKey(h => h.MainId).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<Labeled>(labeled =>
        {
            labeled.ToTable("Labeled");
            labeled.HasMany(l => l.Stickers).WithOne(s => s.Owner).HasForeignKey(s => s.OwnerId);
        });
        modelBuilder.Entity<Box>();
        modelBuilder.Entity<Bag>();
        modelBuilder.Entity<Sticker>(sticker => sticker.ToTable("Stickers"));
        modelBuilder.Entity<Country>(country => country.ToTable("Countries").HasKey(c => c.IsoCode));
        modelBuilder.Entity<Rank>(rank =>
        {
            rank.ToTable("Ranks").HasKey(r => r.Number);
            rank.Property(r => r.Number).ValueGeneratedNever();
        });
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
        context.Services.AddODataEntity<Badge>("Badges", ODataApp.ViewGadgets);
        context.Services.AddScoped<IODataEntityFilter<Badge>, HideSecretBadges>();
        context.Services.AddODataEntity<Tray>("Trays", ODataApp.ViewGadgets);
        context.Services.AddGlobalSearch<Tray>(s => s.Fields(t => t.Bin!.Name, t => t.Name).Page("/trays"));
        context.Services.AddODataEntity<Peg>("Pegs", ODataApp.ViewGadgets);
        context.Services.AddScoped<IODataEntityFilter<Peg>, PegsWhenAny>();
        context.Services.AddSingleton<Coworkee.OData.Computed.IODataComputedPropertySource, BinVolume>();
        context.Services.AddODataEntity<Coupon>("Coupons", ODataApp.ViewGadgets, o => o.Key(c => c.Code));
        context.Services.AddODataEntity<Hook>("Hooks", ODataApp.ViewGadgets, o => o.HideUnless(h => h.MainId, ODataApp.SeeCategories));
        context.Services.AddODataEntity<Brand>("Brands", ODataApp.ViewGadgets, o => o.Key(b => b.Code));
        context.Services.AddScoped<IODataEntityFilter<Brand>, HideSecretBrands>();
        context.Services.AddODataImport("Coupons", (Guid? code, CouponRow row) => new SaveCouponCommand(code, row));
        context.Services.AddODataEntity<Box>("Boxes", ODataApp.ViewGadgets, o => o.Hide(b => b.Stickers, b => b.Code).HideUnless(b => b.Note, ODataApp.SeeCategories));
        context.Services.AddScoped<IODataEntityFilter<Box>, HideSecretBoxes>();
        context.Services.AddODataEntity<Bag>("Bags", ODataApp.ViewGadgets);
        context.Services.AddODataEntity<Sticker>("Stickers", ODataApp.ViewGadgets, o => o.HideUnless(s => s.Weight, ODataApp.SeeCategories).HideUnless(s => s.CreatedBy, ODataApp.SeeCategories));
        context.Services.AddODataEntity<Country>("Countries", ODataApp.ViewGadgets, o => o.Key(c => c.IsoCode));
        context.Services.AddGlobalSearch<Country>(s => s.Fields(c => c.Name).Page("/countries"));
        context.Services.AddODataEntity<Rank>("Ranks", ODataApp.ViewGadgets, o => o.Key(r => r.Number));
        context.Services.AddDbContext<ParcelDbContext>((provider, options) =>
            options.UseCoworkeeDatabase(provider.GetRequiredService<IConfiguration>(), "test"));
        context.Services.AddCoworkeeExternalDbContext<ParcelDbContext>();
        context.Services.AddODataEntity<Parcel>("Parcels", ODataApp.ViewGadgets);
        context.Services.AddScoped<IODataEntityFilter<Parcel>, HideSecretParcels>();
        context.Services.AddSingleton<Coworkee.OData.Computed.IODataComputedPropertySource, ParcelLabel>();
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
