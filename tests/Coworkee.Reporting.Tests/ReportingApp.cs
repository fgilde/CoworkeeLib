using System.Net.Http.Json;
using Coworkee.AspNetCore;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Modularity;
using Coworkee.Core.Security;
using Coworkee.Identity.Setup;
using Coworkee.Infrastructure.Persistence;
using Coworkee.OData;
using Coworkee.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using System.Collections.Concurrent;
using Coworkee.Contracts.Realtime;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

[assembly: AssemblyFixture(typeof(Coworkee.Reporting.Tests.ReportingApp))]
[assembly: CollectionBehavior(CollectionBehavior.CollectionPerAssembly)]

namespace Coworkee.Reporting.Tests;

public sealed class ReportingApp : DatabaseFixture
{
    public const string ViewSales = "Sales.View";
    public const string ViewShoppers = "Shoppers.View";
    public const string SeeQuantities = "Sales.Quantities";

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
        builder.Configuration["Coworkee:Database:Provider"] = Provider;
        builder.Configuration["Coworkee:Notifications:PublicAppUrl"] = "https://app.test";
        builder.AddCoworkee<SalesReportingModule>();
        builder.Services.AddTestAuthentication();
        App = builder.Build();
        await using (var scope = App.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<ReportingTestDbContext>().Database.EnsureCreatedAsync();
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
        App.Services.GetRequiredService<SystemStateCache>().Reset();
        var response = await App.GetTestClient().PostAsJsonAsync("/api/v1/setup/complete",
            new CompleteSetupRequest("token", "Acme", "admin@acme.test", "Admin#12345", "Ada", "Admin"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SetupResultDto>())!;
    }

    public HttpClient As(Guid userId, Guid tenantId) => App.GetTestClient().AsUser(userId, tenantId);

    public async Task<(HubConnection Connection, ConcurrentQueue<RealtimeEnvelope> Events)> ConnectAsync(Guid userId, Guid tenantId, string topic)
    {
        var server = App.GetTestServer();
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(server.BaseAddress, "hubs/realtime"), options =>
            {
                options.HttpMessageHandlerFactory = _ => server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
                options.Headers["Authorization"] = $"{TestAuthHandler.SchemeName} {userId};{tenantId}";
            })
            .Build();
        var events = new ConcurrentQueue<RealtimeEnvelope>();
        connection.On<RealtimeEnvelope>(RealtimeHubMethods.OnEvent, events.Enqueue);
        await connection.StartAsync();
        await connection.InvokeAsync(RealtimeHubMethods.Subscribe, topic);
        return (connection, events);
    }

    public async Task InDbAsync(Guid? tenantId, Func<ReportingTestDbContext, Task> action)
    {
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(null, tenantId));
        await using var scope = App.Services.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<ReportingTestDbContext>());
    }
}

public sealed class ReportingTestDbContext(DbContextOptions<ReportingTestDbContext> options, ICurrentUser currentUser, IEnumerable<IModelContributor> contributors)
    : CoworkeeDbContext(options, currentUser, contributors);

internal sealed class SalesModelContributor : IModelContributor
{
    public void Apply(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Sale>(sale => sale.ToTable("Sales"));
        modelBuilder.Entity<Shopper>(shopper => shopper.ToTable("Shoppers"));
        modelBuilder.Entity<Store>(store => store.ToTable("Stores").HasKey(s => s.Code)); // OpenedAt: see KeyedSetReportTests
        modelBuilder.Entity<Store>().Property(s => s.OpenedAt).HasConversion<string?>();
    }
}

internal sealed class SalesPermissions : Coworkee.Application.Authorization.IPermissionDefinitionContributor
{
    public void Define(Coworkee.Application.Authorization.PermissionDefinitionContext context) =>
        context.Group("Sales", "Sales").Add(ReportingApp.ViewSales, "View sales").Add(ReportingApp.ViewShoppers, "View shoppers").Add(ReportingApp.SeeQuantities, "See quantities").Add(MonthlyNumbers.ViewNumbers, "View numbers");
}

/// <summary>Rows of the region "Hidden" never leave the server, like an app's row filter.</summary>
internal sealed class HideHiddenRegion : IODataEntityFilter<Sale>
{
    public Task<IQueryable<Sale>> ApplyAsync(IQueryable<Sale> query, CancellationToken cancellationToken) =>
        Task.FromResult(query.Where(s => s.Region != "Hidden"));
}

/// <summary>A row filter that depends on the user, like a sales territory: the users in <see cref="Users"/> see the region South only.</summary>
internal sealed class SouthOnlyFilter(ICurrentUser currentUser) : IODataEntityFilter<Sale>
{
    public static readonly ConcurrentDictionary<Guid, bool> Users = new();

    public Task<IQueryable<Sale>> ApplyAsync(IQueryable<Sale> query, CancellationToken cancellationToken) =>
        Task.FromResult(currentUser.UserId is { } id && Users.ContainsKey(id) ? query.Where(s => s.Region == "South") : query);
}

/// <summary>A row filter on a navigation target: the users in <see cref="Users"/> do not see "Shopper 7".</summary>
internal sealed class HideShopperSeven(ICurrentUser currentUser) : IODataEntityFilter<Shopper>
{
    public static readonly ConcurrentDictionary<Guid, bool> Users = new();

    public Task<IQueryable<Shopper>> ApplyAsync(IQueryable<Shopper> query, CancellationToken cancellationToken) =>
        Task.FromResult(currentUser.UserId is { } id && Users.ContainsKey(id) ? query.Where(s => s.Name != "Shopper 7") : query);
}

[DependsOn(typeof(CoworkeeReportingModule))]
public sealed class SalesReportingModule : CoworkeeModule
{
    public override void ConfigureServices(ModuleServiceContext context)
    {
        context.Services.AddSingleton<IModelContributor, SalesModelContributor>();
        context.Services.AddSingleton<Coworkee.Application.Authorization.IPermissionDefinitionContributor, SalesPermissions>();
        context.Services.AddCoworkeeDbContext<ReportingTestDbContext>((provider, options) =>
            options.UseCoworkeeDatabase(provider.GetRequiredService<IConfiguration>(), "test"));
        context.Services.AddODataEntity<Sale>("Sales", ReportingApp.ViewSales, o => o.Hide(s => s.Secret).HideUnless(s => s.Quantity, ReportingApp.SeeQuantities));
        context.Services.AddODataEntity<Shopper>("Shoppers", ReportingApp.ViewShoppers);
        context.Services.AddODataEntity<Store>("Stores", ReportingApp.ViewShoppers, o => o.Key(s => s.Code));
        context.Services.AddScoped<IODataEntityFilter<Sale>, HideHiddenRegion>();
        context.Services.AddScoped<IODataEntityFilter<Sale>, SouthOnlyFilter>();
        context.Services.AddScoped<IODataEntityFilter<Shopper>, HideShopperSeven>();
        context.Services.AddSingleton<TestDefaultLanguage>();
        context.Services.AddScoped<Coworkee.Application.Localization.IDefaultLanguage>(sp => sp.GetRequiredService<TestDefaultLanguage>());
        context.Services.AddSingleton<FakeAi>();
        context.Services.AddSingleton<Coworkee.Application.Ai.IAiTextGenerator>(provider => provider.GetRequiredService<FakeAi>());
        context.Services.AddReportSource<MonthlyNumber>("MonthlyNumbers", "Monthly numbers", MonthlyNumbers.ViewNumbers, MonthlyNumbers.RowsAsync);
        context.Services.AddReportSource<SaleRow>("SaleRows", "Sale rows", MonthlyNumbers.ViewNumbers, MonthlyNumbers.SalesAsync);
    }
}

/// <summary>The organisation's default language of the tests; null like an app without one.</summary>
public sealed class TestDefaultLanguage : Coworkee.Application.Localization.IDefaultLanguage
{
    public string? Culture { get; set; }

    public Task<string?> GetAsync(CancellationToken cancellationToken) => Task.FromResult(Culture);
}
