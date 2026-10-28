using System.Collections.Concurrent;
using System.Net.Http.Json;
using Coworkee.AspNetCore;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Notifications;
using Coworkee.Contracts.Realtime;
using Coworkee.Core.Modularity;
using Coworkee.Core.Security;
using Coworkee.Domain;
using Coworkee.Identity;
using Coworkee.Identity.Setup;
using Coworkee.Infrastructure.Outbox;
using Coworkee.Infrastructure.Persistence;
using Coworkee.OData;
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

public sealed class NotificationApp : DatabaseFixture
{
    protected override string[] SchemasToExclude => ["hangfire"];

    public WebApplication App { get; private set; } = null!;

    /// <summary>Takes the pushes instead of the push services.</summary>
    public FakePushSender PushSender { get; } = new();

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration["Coworkee:SetupToken"] = "token";
        builder.Configuration["ConnectionStrings:test"] = ConnectionString;
        builder.Configuration["Coworkee:Database:Provider"] = Provider;
        builder.Configuration["Coworkee:Notifications:PublicAppUrl"] = "https://app.test";
        builder.Configuration["Coworkee:Jobs:ConnectionStringName"] = "test";

        // mails stay queued in the database; no job server sends them
        builder.Configuration["Coworkee:Jobs:RunServer"] = "false";
        var (publicKey, privateKey) = Push.WebPushSender.CreateKeys();
        builder.Configuration["Coworkee:Notifications:Push:PublicKey"] = publicKey;
        builder.Configuration["Coworkee:Notifications:Push:PrivateKey"] = privateKey;
        builder.AddCoworkee<TestNotificationModule>();
        builder.Services.AddTestAuthentication();
        builder.Services.AddSingleton<Push.IPushSender>(PushSender);
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

    public async Task<Guid> AddShipmentAsync(Guid tenantId, string number = "S-1")
    {
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(null, tenantId));
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationTestDbContext>();
        var shipment = new Shipment { Number = number, TenantId = tenantId };
        db.Add(shipment);
        await db.SaveChangesAsync();
        return shipment.Id;
    }

    /// <summary>Publishes as <paramref name="actor"/>; <paramref name="clock"/> stands in for the time when given.</summary>
    public async Task PublishAsync(Guid? actor, Guid? tenantId, NotificationMessage message, TimeProvider? clock = null)
    {
        using var scopeActor = CurrentUserScope.Begin(new ImpersonatedUser(actor, tenantId));
        await using var scope = App.Services.CreateAsyncScope();
        var notifier = clock is null
            ? scope.ServiceProvider.GetRequiredService<INotifier>()
            : ActivatorUtilities.CreateInstance<Notifier>(scope.ServiceProvider, clock);
        await notifier.PublishAsync(message);
        await scope.ServiceProvider.GetRequiredService<NotificationTestDbContext>().SaveChangesAsync();
        await ProcessOutboxAsync();
    }

    /// <summary>Hands the committed domain events to their handlers, as the outbox service of the API host does.</summary>
    public Task<int> ProcessOutboxAsync() => App.Services.GetRequiredService<OutboxProcessor<NotificationTestDbContext>>().ProcessBatchAsync(100, CancellationToken.None);

    public async Task<bool> ClaimAsync(PendingNotificationMail seen, TimeProvider clock)
    {
        await using var scope = App.Services.CreateAsyncScope();
        return await ActivatorUtilities.CreateInstance<NotificationMailJob>(scope.ServiceProvider, clock).ClaimAsync(seen, CancellationToken.None);
    }

    public async Task RunMailJobAsync(TimeProvider clock)
    {
        await using var scope = App.Services.CreateAsyncScope();
        await ActivatorUtilities.CreateInstance<NotificationMailJob>(scope.ServiceProvider, clock).ExecuteAsync(CancellationToken.None);
    }

    public async Task<T> InDbAsync<T>(Guid? tenantId, Func<NotificationTestDbContext, Task<T>> action)
    {
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(null, tenantId));
        await using var scope = App.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<NotificationTestDbContext>());
    }

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

public sealed class FakePushSender : Push.IPushSender
{
    public ConcurrentQueue<(Push.PushSubscription Device, string Payload)> Sent { get; } = new();

    public Push.PushResult Answer { get; set; } = Push.PushResult.Sent;

    public Task<Push.PushResult> SendAsync(Push.PushSubscription subscription, string payload, CancellationToken cancellationToken)
    {
        Sent.Enqueue((subscription, payload));
        return Task.FromResult(Answer);
    }
}

public sealed class NotificationTestDbContext(DbContextOptions<NotificationTestDbContext> options, ICurrentUser currentUser, IEnumerable<IModelContributor> contributors)
    : CoworkeeDbContext(options, currentUser, contributors);

/// <summary>A followable record: the Shipments set needs <see cref="ViewShipments"/>, and hidden shipments are seen by nobody.</summary>
public sealed class Shipment : AuditedEntity, IMultiTenant
{
    public const string ViewShipments = "Shipments.View";
    public const string Hidden = "hidden";

    public required string Number { get; set; }

    public Guid TenantId { get; set; }
}

/// <summary>Hidden shipments for everyone, and every shipment for the <see cref="OutOfScope"/> users (as if they left the shipments' group).</summary>
internal sealed class HiddenShipments(ICurrentUser currentUser) : Coworkee.OData.IODataEntityFilter<Shipment>
{
    public static readonly ConcurrentDictionary<Guid, bool> OutOfScope = new();

    public Task<IQueryable<Shipment>> ApplyAsync(IQueryable<Shipment> query, CancellationToken cancellationToken) =>
        Task.FromResult(currentUser.UserId is { } me && OutOfScope.ContainsKey(me) ? query.Where(_ => false) : query.Where(s => s.Number != Shipment.Hidden));
}

/// <summary>The mail of <see cref="TestNotificationModule.Milestone"/>: the model's values next to <c>user</c> and <c>notification</c>.</summary>
internal sealed class MilestoneMailTemplate : Coworkee.Mailing.IMailTemplateContributor
{
    public void Define(Coworkee.Mailing.MailTemplateContext context) =>
        context.Add(new Coworkee.Mailing.MailTemplateDefinition(TestNotificationModule.MilestoneTemplate, "Milestone",
            NotificationMailModel.Sample(new { shipment = new { Number = "S-9" }, milestone_name = "ETA", OldValue = "1 May", new_value = "2 May", comment = "On time" }),
            new Dictionary<string, Coworkee.Mailing.MailTemplateContent>
            {
                ["en"] = new("{{ shipment.number }}: {{ milestone_name }}",
                    "<p>{{ user.email }}|{{ old_value }}|{{ new_value }}|{{ comment }}|{{ notification.title }}|{{ notification.link }}</p>"),
            }));
}

internal sealed class ShipmentModel : IModelContributor
{
    public void Apply(ModelBuilder modelBuilder) => modelBuilder.Entity<Shipment>(shipment => shipment.ToTable("Shipments"));
}

internal sealed class ShipmentPermissions : Coworkee.Application.Authorization.IPermissionDefinitionContributor
{
    public void Define(Coworkee.Application.Authorization.PermissionDefinitionContext context) =>
        context.Group("Shipments", "Shipments").Add(Shipment.ViewShipments, "View shipments");
}

[DependsOn(typeof(CoworkeeNotificationsModule), typeof(CoworkeeIdentityModule), typeof(Coworkee.Localization.CoworkeeLocalizationModule), typeof(Coworkee.OData.CoworkeeODataModule))]
public sealed class TestNotificationModule : CoworkeeModule
{
    public const string Eta = "shipment.eta";
    public const string Status = "shipment.status";
    public const string Milestone = "shipment.milestone";
    public const string Missing = "shipment.missing";
    public const string MilestoneTemplate = "Test.Milestone";

    /// <summary>The mail delay of <see cref="Milestone"/> per tenant, as a tenant feature would hold it; a tenant without one has the type's default.</summary>
    public static readonly ConcurrentDictionary<Guid, TimeSpan> MilestoneDelays = new();

    public override void ConfigureServices(ModuleServiceContext context)
    {
        context.Services.AddCoworkeeDbContext<NotificationTestDbContext>((provider, options) =>
            options.UseCoworkeeDatabase(provider.GetRequiredService<IConfiguration>(), "test"));

        // the tests run the outbox themselves
        context.Services.AddSingleton<OutboxProcessor<NotificationTestDbContext>>();
        context.Services.AddSingleton<IModelContributor, ShipmentModel>();
        context.Services.AddSingleton<Coworkee.Application.Authorization.IPermissionDefinitionContributor, ShipmentPermissions>();
        context.Services.AddODataEntity<Shipment>("Shipments", Shipment.ViewShipments);
        context.Services.AddScoped<Coworkee.OData.IODataEntityFilter<Shipment>, HiddenShipments>();
        context.Services.AddSingleton<Coworkee.Mailing.IMailTemplateContributor, MilestoneMailTemplate>();
        context.Services.AddNotificationTypes(n => n
            .Add(Eta, "Shipment", "Estimated arrival changed", NotificationScope.Followed, inApp: true, email: true, emailDelay: TimeSpan.FromMinutes(1))
            .Add(Status, "Shipment", "Status changed", NotificationScope.Off)
            .Add(Milestone, "Shipment", "Milestone changed", NotificationScope.All, email: true, emailDelay: TimeSpan.FromMinutes(1), mailTemplate: MilestoneTemplate,
                resolveEmailDelay: (services, _) => Task.FromResult(
                    MilestoneDelays.TryGetValue(services.GetRequiredService<ICurrentUser>().TenantId ?? Guid.Empty, out var delay) ? delay : (TimeSpan?)null))
            .Add(Missing, "Shipment", "Template gone", NotificationScope.All, inApp: false, email: true, mailTemplate: "Test.Gone")
            .Followable<Shipment>("Shipments", id => $"/shipments/{id}"));
    }
}
