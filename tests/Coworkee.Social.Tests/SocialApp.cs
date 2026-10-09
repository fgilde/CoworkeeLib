using System.Collections.Concurrent;
using System.Net.Http.Json;
using Coworkee.AspNetCore;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Realtime;
using Coworkee.Core.Modularity;
using Coworkee.Core.Security;
using Coworkee.Domain;
using Coworkee.Identity;
using Coworkee.Identity.Setup;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Notifications;
using Coworkee.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

[assembly: AssemblyFixture(typeof(Coworkee.Social.Tests.SocialApp))]
[assembly: CollectionBehavior(CollectionBehavior.CollectionPerAssembly)]

namespace Coworkee.Social.Tests;

public sealed class SocialApp : PostgresFixture
{
    protected override string[] SchemasToExclude => ["hangfire"];

    public WebApplication App { get; private set; } = null!;

    /// <summary>Notes with this text are hidden from everyone by the app's access check.</summary>
    public const string Secret = "secret";

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration["Coworkee:SetupToken"] = "token";
        builder.Configuration["ConnectionStrings:test"] = ConnectionString;
        builder.Configuration["Coworkee:Jobs:ConnectionStringName"] = "test";
        builder.Configuration["Coworkee:Jobs:RunServer"] = "false";
        builder.AddCoworkee<TestSocialModule>();
        builder.Services.AddTestAuthentication();
        App = builder.Build();
        await using (var scope = App.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<SocialTestDbContext>().Database.EnsureCreatedAsync();
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

    public async Task<Guid> AddNoteAsync(Guid? userId, Guid tenantId, string text = "note")
    {
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(userId, tenantId));
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SocialTestDbContext>();
        var note = new Note { Text = text, TenantId = tenantId };
        db.Add(note);
        await db.SaveChangesAsync();
        return note.Id;
    }

    public async Task<List<Notification>> NotificationsAsync()
    {
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(null, null));
        await using var scope = App.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<SocialTestDbContext>().Set<Notification>().ToListAsync();
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
        return (connection, events);
    }
}

public sealed class Note : AuditedEntity, IMultiTenant
{
    public required string Text { get; set; }

    public Guid TenantId { get; set; }
}

public sealed class SocialTestDbContext(DbContextOptions<SocialTestDbContext> options, ICurrentUser currentUser, IEnumerable<IModelContributor> contributors)
    : CoworkeeDbContext(options, currentUser, contributors);

internal sealed class NoteModelContributor : IModelContributor
{
    public void Apply(ModelBuilder modelBuilder) => modelBuilder.Entity<Note>(note => note.ToTable("Notes"));
}

[DependsOn(typeof(CoworkeeSocialModule), typeof(CoworkeeNotificationsModule), typeof(CoworkeeIdentityModule))]
public sealed class TestSocialModule : CoworkeeModule
{
    public override void ConfigureServices(ModuleServiceContext context)
    {
        context.Services.AddSingleton<IModelContributor, NoteModelContributor>();
        context.Services.AddCoworkeeSocial(social => social
            .Comments<Note>("Notes", id => $"/notes/{id}")
            .Tags<Note>("Notes")
            .Ratings<Note>("Notes")
            .Authorize(async (services, access, ct) => access.EntityId is not { } id
                || !await services.GetRequiredService<CoworkeeDbContext>().Set<Note>().AnyAsync(n => n.Id == id && n.Text == SocialApp.Secret, ct)));
        context.Services.AddCoworkeeDbContext<SocialTestDbContext>((provider, options) =>
            options.UseNpgsql(provider.GetRequiredService<IConfiguration>().GetConnectionString("test")));
    }
}
