using System.Collections.Concurrent;
using System.Net.Http.Json;
using Coworkee.AspNetCore;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Realtime;
using Coworkee.Core.Modularity;
using Coworkee.Core.Security;
using Coworkee.Identity;
using Coworkee.Identity.Setup;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

[assembly: AssemblyFixture(typeof(Coworkee.Notifications.Tests.NotificationApp))]
[assembly: CollectionBehavior(CollectionBehavior.CollectionPerAssembly)]

namespace Coworkee.Notifications.Tests;

public sealed class NotificationApp : PostgresFixture
{
    public WebApplication App { get; private set; } = null!;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration["Coworkee:SetupToken"] = "token";
        builder.Configuration["ConnectionStrings:test"] = ConnectionString;
        builder.AddCoworkee<TestNotificationModule>();
        builder.Services.AddTestAuthentication();
        App = builder.Build();
        await using (var scope = App.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<NotificationTestDbContext>().Database.EnsureCreatedAsync();
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

    public async Task NotifyAsync(Guid? actor, Guid? tenantId, params Guid[] users)
    {
        using var scopeActor = CurrentUserScope.Begin(new ImpersonatedUser(actor, tenantId));
        await using var scope = App.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<INotifier>().NotifyAsync(users, "test", "Document shared", "Grace shared a file.", "/d/1");
        await scope.ServiceProvider.GetRequiredService<NotificationTestDbContext>().SaveChangesAsync();
    }

    public async Task<(HubConnection Connection, ConcurrentQueue<RealtimeEnvelope> Events)> ConnectAsync(Guid userId, Guid tenantId)
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
        await connection.InvokeAsync(RealtimeHubMethods.Subscribe, RealtimeTopics.User(userId));
        return (connection, events);
    }
}

public sealed class NotificationTestDbContext(DbContextOptions<NotificationTestDbContext> options, ICurrentUser currentUser, IEnumerable<IModelContributor> contributors)
    : CoworkeeDbContext(options, currentUser, contributors);

[DependsOn(typeof(CoworkeeNotificationsModule), typeof(CoworkeeIdentityModule))]
public sealed class TestNotificationModule : CoworkeeModule
{
    public override void ConfigureServices(ModuleServiceContext context) =>
        context.Services.AddCoworkeeDbContext<NotificationTestDbContext>((provider, options) =>
            options.UseNpgsql(provider.GetRequiredService<IConfiguration>().GetConnectionString("test")));
}
