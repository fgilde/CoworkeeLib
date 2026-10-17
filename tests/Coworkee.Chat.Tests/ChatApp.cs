using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Coworkee.Application.GlobalSearch;
using Coworkee.Contracts.Search;
using Coworkee.Notifications;
using System.Net.Http.Json;
using Coworkee.AspNetCore;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Realtime;
using Coworkee.Core.Modularity;
using Coworkee.Core.Security;
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

[assembly: AssemblyFixture(typeof(Coworkee.Chat.Tests.ChatApp))]
[assembly: CollectionBehavior(CollectionBehavior.CollectionPerAssembly)]

namespace Coworkee.Chat.Tests;

public sealed class ChatApp : DatabaseFixture
{
    private readonly string _blobs = Path.Combine(Path.GetTempPath(), "coworkee-chat-tests", Guid.NewGuid().ToString("N"));

    public WebApplication App { get; private set; } = null!;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration["Coworkee:SetupToken"] = "token";
        builder.Configuration["ConnectionStrings:test"] = ConnectionString;
        builder.Configuration["Coworkee:Database:Provider"] = Provider;
        builder.Configuration["Coworkee:Storage:FileSystem:Root"] = _blobs;
        builder.Configuration["Coworkee:Jobs:ConnectionStringName"] = "test";
        builder.Configuration["Coworkee:Jobs:RunServer"] = "false";
        builder.AddCoworkee<TestChatModule>();
        builder.Services.AddTestAuthentication();
        App = builder.Build();
        await using (var scope = App.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<ChatTestDbContext>().Database.EnsureCreatedAsync();
        }

        App.UseCoworkee();
        await App.StartAsync();
    }

    public override async ValueTask DisposeAsync()
    {
        await App.DisposeAsync();
        await base.DisposeAsync();
        if (Directory.Exists(_blobs))
        {
            Directory.Delete(_blobs, true);
        }
    }

    public async Task<List<Notification>> NotificationsAsync()
    {
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(null, null));
        await using var scope = App.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ChatTestDbContext>().Set<Notification>().ToListAsync();
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

    public async Task<UserDto> CreateUserAsync(SetupResultDto setup, string email, params string[] permissions)
    {
        var admin = As(setup.AdminUserId, setup.TenantId);
        var ct = TestContext.Current.CancellationToken;
        var response = await admin.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest(email, "Passw0rd!x", null, null), ct);
        response.EnsureSuccessStatusCode();
        var user = (await response.Content.ReadFromJsonAsync<UserDto>(ct))!;
        if (permissions.Length > 0)
        {
            var role = await (await admin.PostAsJsonAsync("/api/v1/identity/roles", new RoleRequest($"Chat {email}", null), ct)).Content.ReadFromJsonAsync<Guid>(ct);
            (await admin.PutAsJsonAsync($"/api/v1/identity/permissions/grants/Role/{role}", new NameListRequest(permissions), ct)).EnsureSuccessStatusCode();
            (await admin.PutAsJsonAsync($"/api/v1/identity/users/{user.Id}/roles", new IdListRequest([role]), ct)).EnsureSuccessStatusCode();
        }

        return user;
    }

    /// <summary>A hub connection that follows the user's own topic.</summary>
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

public sealed class ChatTestDbContext(DbContextOptions<ChatTestDbContext> options, ICurrentUser currentUser, IEnumerable<IModelContributor> contributors)
    : CoworkeeDbContext(options, currentUser, contributors);

[DependsOn(typeof(CoworkeeChatModule), typeof(CoworkeeNotificationsModule))]
public sealed class TestChatModule : CoworkeeModule
{
    public override void ConfigureServices(ModuleServiceContext context)
    {
        context.Services.AddCoworkeeDbContext<ChatTestDbContext>((provider, options) =>
            options.UseCoworkeeDatabase(provider.GetRequiredService<IConfiguration>(), "test"));
        context.Services.AddGlobalSearchProvider<TestRecordProvider>();
    }
}

/// <summary>Records only their reader may find: the provider "records" knows each id's single reader.</summary>
public sealed class TestRecordProvider : IGlobalSearchProvider
{
    public static readonly ConcurrentDictionary<Guid, (Guid Reader, string Title)> Records = new();

    public string Name => "records";

    public string Group => "Records";

    public string? Icon => null;

    public string? Permission => null;

    public async IAsyncEnumerable<GlobalSearchHit> SearchAsync(GlobalSearchQuery query, GlobalSearchContext context, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.Yield();
        var me = context.Services.GetRequiredService<ICurrentUser>().UserId;
        foreach (var (id, record) in Records)
        {
            if (record.Reader == me && (query.Id == id || record.Title.Contains(query.Text, StringComparison.OrdinalIgnoreCase)))
            {
                yield return new GlobalSearchHit(Name, Group, record.Title) { Id = id.ToString(), Url = $"/records/{id}", Score = 1 };
            }
        }
    }
}
