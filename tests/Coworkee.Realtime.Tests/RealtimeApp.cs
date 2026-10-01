using System.Collections.Concurrent;
using System.Net.Http.Json;
using Coworkee.Application.Authorization;
using Coworkee.AspNetCore;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Realtime;
using Coworkee.Core.Modularity;
using Coworkee.Core.Security;
using Coworkee.Domain;
using Coworkee.Identity;
using Coworkee.Identity.Domain;
using Coworkee.Identity.Setup;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

[assembly: AssemblyFixture(typeof(Coworkee.Realtime.Tests.RealtimeApp))]
[assembly: CollectionBehavior(CollectionBehavior.CollectionPerAssembly)]

namespace Coworkee.Realtime.Tests;

public sealed class RealtimeApp : PostgresFixture
{
    public WebApplication App { get; private set; } = null!;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration["Coworkee:SetupToken"] = "token";
        builder.Configuration["ConnectionStrings:test"] = ConnectionString;
        builder.AddCoworkee<TestRealtimeModule>();
        builder.Services.AddTestAuthentication();
        App = builder.Build();
        await using (var scope = App.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<RealtimeTestDbContext>().Database.EnsureCreatedAsync();
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

    public async Task<Listener> ConnectAsync(Guid userId, Guid tenantId, DateTimeOffset? expires = null)
    {
        var server = App.GetTestServer();
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(server.BaseAddress, "hubs/realtime"), options =>
            {
                options.HttpMessageHandlerFactory = _ => server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
                options.Headers["Authorization"] = $"{TestAuthHandler.SchemeName} {userId};{tenantId};{expires?.ToUnixTimeSeconds()}";
            })
            .Build();
        var listener = new Listener(connection);
        await connection.StartAsync();
        return listener;
    }

    public async Task<T> AsActorAsync<T>(Guid? userId, Guid? tenantId, Func<RealtimeTestDbContext, Task<T>> action)
    {
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(userId, tenantId));
        await using var scope = App.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<RealtimeTestDbContext>());
    }

    public Task<(Guid UserId, Guid TenantId)> CreateTenantAdminAsync() => AsActorAsync(null, null, async db =>
    {
        var tenant = new Tenant { Name = "Other", Identifier = "other-" + Guid.NewGuid().ToString("N")[..8] };
        var email = $"admin-{Guid.NewGuid():N}@other.test";
        var user = new User { TenantId = tenant.Id, UserName = email, NormalizedUserName = email.ToUpperInvariant(), Email = email, NormalizedEmail = email.ToUpperInvariant() };
        var adminRole = await db.Set<Role>().Where(r => r.IsSystem && r.Name == SystemRoles.Admin).Select(r => r.Id).SingleAsync();
        db.AddRange(tenant, user, new IdentityUserRole<Guid> { UserId = user.Id, RoleId = adminRole });
        await db.SaveChangesAsync();
        return (user.Id, tenant.Id);
    });
}

public sealed class Listener : IAsyncDisposable
{
    public Listener(HubConnection connection)
    {
        Connection = connection;
        connection.On<RealtimeEnvelope>(RealtimeHubMethods.OnEvent, Events.Enqueue);
    }

    public HubConnection Connection { get; }

    public ConcurrentQueue<RealtimeEnvelope> Events { get; } = new();

    public Task SubscribeAsync(string topic) => Connection.InvokeAsync(RealtimeHubMethods.Subscribe, topic);

    public async Task<RealtimeEnvelope> NextAsync(Func<RealtimeEnvelope, bool> match, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(15));
        while (DateTime.UtcNow < deadline)
        {
            if (Events.FirstOrDefault(match) is { } found)
            {
                return found;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException("No matching realtime event.");
    }

    public async ValueTask DisposeAsync() => await Connection.DisposeAsync();
}

[Realtime(Ticket.ViewPermission, ResourceType = "Ticket")]
public class Ticket : AggregateRoot, IMultiTenant
{
    public const string ViewPermission = "Test.Tickets.View";

    public required string Title { get; set; }

    public Guid TenantId { get; set; }
}

public sealed class UrgentTicket : Ticket
{
    public int Priority { get; set; }
}

public sealed class RealtimeTestDbContext(DbContextOptions<RealtimeTestDbContext> options, ICurrentUser currentUser, IEnumerable<IModelContributor> contributors)
    : CoworkeeDbContext(options, currentUser, contributors);

internal sealed class TicketModel : IModelContributor, IPermissionDefinitionContributor
{
    public void Apply(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Ticket>().Property(t => t.Title).HasMaxLength(20);
        modelBuilder.Entity<UrgentTicket>();
    }

    public void Define(PermissionDefinitionContext context) => context.Group("Test", "Test").Add(Ticket.ViewPermission, "View tickets");
}

[DependsOn(typeof(CoworkeeRealtimeModule), typeof(CoworkeeIdentityModule))]
public sealed class TestRealtimeModule : CoworkeeModule
{
    public override void ConfigureServices(ModuleServiceContext context)
    {
        context.Services.AddSingleton<TicketModel>();
        context.Services.AddSingleton<IModelContributor>(sp => sp.GetRequiredService<TicketModel>());
        context.Services.AddSingleton<IPermissionDefinitionContributor>(sp => sp.GetRequiredService<TicketModel>());
        context.Services.AddCoworkeeDbContext<RealtimeTestDbContext>((provider, options) =>
            options.UseNpgsql(provider.GetRequiredService<IConfiguration>().GetConnectionString("test")));
    }
}
