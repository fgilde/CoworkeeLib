using System.Collections.Concurrent;
using System.Net.Http.Json;
using Coworkee.Application.Authorization;
using Coworkee.AspNetCore;
using Coworkee.BackgroundJobs;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Modularity;
using Coworkee.Core.Security;
using Coworkee.Domain;
using Coworkee.Identity;
using Coworkee.Identity.Setup;
using Coworkee.Infrastructure.Outbox;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Notifications;
using Coworkee.OData;
using Coworkee.ResponseFilters;
using Coworkee.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Nextended.ResponseFilters;

[assembly: AssemblyFixture(typeof(Coworkee.Webhooks.Tests.WebhooksApp))]
[assembly: CollectionBehavior(CollectionBehavior.CollectionPerAssembly)]

namespace Coworkee.Webhooks.Tests;

/// <summary>An app with webhooks and gadgets, a receiver on a local port, a fake clock, and jobs and outbox run by the test.</summary>
public sealed class WebhooksApp : DatabaseFixture
{
    protected override string[] SchemasToExclude => ["hangfire"];

    public WebApplication App { get; private set; } = null!;

    public WebApplication Receiver { get; private set; } = null!;

    public string ReceiverUrl { get; private set; } = "";

    // whole seconds: the database keeps microseconds only
    public FakeTimeProvider Clock { get; } = new(DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds()));

    public ConcurrentQueue<ReceivedWebhook> Received { get; } = new();

    public int ReceiverStatus { get; set; } = 200;

    public JobLog Jobs { get; } = new();

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await StartReceiverAsync();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration["Coworkee:SetupToken"] = "token";
        builder.Configuration["ConnectionStrings:test"] = ConnectionString;
        builder.Configuration["Coworkee:Database:Provider"] = Provider;
        builder.Configuration["Coworkee:Jobs:ConnectionStringName"] = "test";
        builder.Configuration["Coworkee:Jobs:RunServer"] = "false";
        builder.Configuration["Coworkee:Webhooks:RequireHttps"] = "false";
        builder.Configuration["Coworkee:Webhooks:AllowPrivateNetworks"] = "true";
        builder.Configuration["Coworkee:Webhooks:AllowedPorts:0"] = new Uri(ReceiverUrl).Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        builder.Configuration["Coworkee:Webhooks:MaxAttempts"] = "3";
        builder.Configuration["Coworkee:Webhooks:DisableAfterFailures"] = "2";
        builder.AddCoworkee<TestWebhooksModule>();
        builder.Services.AddTestAuthentication();
        builder.Services.AddSingleton<TimeProvider>(Clock);
        builder.Services.AddSingleton(Jobs);
        builder.Services.AddScoped<IBackgroundJobs, RecordingJobs>();
        builder.Services.AddSingleton<OutboxProcessor<WebhooksTestDbContext>>();
        App = builder.Build();
        await using (var scope = App.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<WebhooksTestDbContext>().Database.EnsureCreatedAsync();
        }

        App.UseCoworkee();
        await App.StartAsync();
    }

    private async Task StartReceiverAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        Receiver = builder.Build();
        Receiver.MapPost("/hook", async (HttpRequest request) =>
        {
            using var reader = new StreamReader(request.Body);
            Received.Enqueue(new ReceivedWebhook(request.QueryString.Value ?? "", request.Headers.ToDictionary(h => h.Key.ToLowerInvariant(), h => h.Value.ToString()), await reader.ReadToEndAsync()));
            return Results.Text("thanks", statusCode: ReceiverStatus);
        });
        await Receiver.StartAsync();
        ReceiverUrl = Receiver.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First() + "/hook";
    }

    public override async ValueTask DisposeAsync()
    {
        await App.DisposeAsync();
        await Receiver.DisposeAsync();
        await base.DisposeAsync();
    }

    public async Task<SetupResultDto> SetupAsync()
    {
        await ResetAsync();
        App.Services.GetRequiredService<SystemStateCache>().Reset();
        Received.Clear();
        Jobs.Clear();
        ReceiverStatus = 200;
        var response = await App.GetTestClient().PostAsJsonAsync("/api/v1/setup/complete",
            new CompleteSetupRequest("token", "Acme", "admin@acme.test", "Admin#12345", "Ada", "Admin"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SetupResultDto>())!;
    }

    public HttpClient As(Guid userId, Guid tenantId) => App.GetTestClient().AsUser(userId, tenantId);

    public async Task<T> WithDbAsync<T>(Guid? userId, Guid tenantId, Func<WebhooksTestDbContext, Task<T>> action)
    {
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(userId, tenantId));
        await using var scope = App.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<WebhooksTestDbContext>());
    }

    /// <summary>Processes the outbox until it is empty, then runs every job that is due; repeats while something happened.</summary>
    public async Task SettleAsync()
    {
        var processor = App.Services.GetRequiredService<OutboxProcessor<WebhooksTestDbContext>>();
        bool busy;
        do
        {
            busy = false;
            while (await processor.ProcessBatchAsync(50, CancellationToken.None) > 0)
            {
                busy = true;
            }

            foreach (var job in Jobs.TakeDue(Clock.GetUtcNow()))
            {
                busy = true;
                using var actor = CurrentUserScope.Begin(new ImpersonatedUser(job.UserId, job.TenantId));
                await using var scope = App.Services.CreateAsyncScope();
                await ActivatorUtilities.CreateInstance<WebhookDeliveryJob>(scope.ServiceProvider).ExecuteAsync(job.DeliveryId, CancellationToken.None);
            }
        }
        while (busy);
    }
}

public sealed record ReceivedWebhook(string Query, Dictionary<string, string> Headers, string Body);

public sealed record RecordedJob(Guid DeliveryId, DateTimeOffset DueAt, TimeSpan Delay, Guid? UserId, Guid? TenantId);

public sealed class JobLog
{
    private readonly List<RecordedJob> _pending = [];

    public List<TimeSpan> Delays { get; } = [];

    public void Add(RecordedJob job)
    {
        lock (_pending)
        {
            _pending.Add(job);
            Delays.Add(job.Delay);
        }
    }

    public List<RecordedJob> TakeDue(DateTimeOffset now)
    {
        lock (_pending)
        {
            var due = _pending.Where(j => j.DueAt <= now).ToList();
            _pending.RemoveAll(due.Contains);
            return due;
        }
    }

    public void Clear()
    {
        lock (_pending)
        {
            _pending.Clear();
            Delays.Clear();
        }
    }
}

internal sealed class RecordingJobs(JobLog log, ICurrentUser currentUser, TimeProvider clock) : IBackgroundJobs
{
    public string Enqueue<TJob, TArgs>(TArgs args, string queue = "default")
        where TJob : IBackgroundJob<TArgs> => Schedule<TJob, TArgs>(args, TimeSpan.Zero, queue);

    public string Schedule<TJob, TArgs>(TArgs args, TimeSpan delay, string queue = "default")
        where TJob : IBackgroundJob<TArgs>
    {
        if (args is Guid id && typeof(TJob) == typeof(WebhookDeliveryJob))
        {
            log.Add(new RecordedJob(id, clock.GetUtcNow() + delay, delay, currentUser.UserId, currentUser.TenantId));
        }

        return Guid.NewGuid().ToString();
    }
}

public static class GadgetPermissions
{
    public const string View = "Gadgets.View";
    public const string Prices = "Gadgets.Prices";
}

public sealed class Gadget : Entity, IMultiTenant
{
    public Guid TenantId { get; set; }

    public required string Name { get; set; }

    public decimal Price { get; set; }

    public decimal Cost { get; set; }
}

public sealed class WebhooksTestDbContext(DbContextOptions<WebhooksTestDbContext> options, ICurrentUser currentUser, IEnumerable<IModelContributor> contributors)
    : CoworkeeDbContext(options, currentUser, contributors);

internal sealed class GadgetModel : IModelContributor
{
    public void Apply(ModelBuilder modelBuilder) => modelBuilder.Entity<Gadget>(gadget => gadget.ToTable("Gadgets"));
}

internal sealed class GadgetPermissionDefinitions : IPermissionDefinitionContributor
{
    public void Define(PermissionDefinitionContext context) =>
        context.Group("Gadgets", "Gadgets").Add(GadgetPermissions.View, "View gadgets").Add(GadgetPermissions.Prices, "See prices");
}

/// <summary>Gadgets named "secret" are no one's to see.</summary>
internal sealed class GadgetRowFilter : IODataEntityFilter<Gadget>
{
    public Task<IQueryable<Gadget>> ApplyAsync(IQueryable<Gadget> query, CancellationToken cancellationToken) => Task.FromResult(query.Where(g => g.Name != "secret"));
}

public sealed class GadgetFilter : ResponseFilter<Gadget>
{
    public GadgetFilter() => Remove(g => g.Price).UnlessGranted(GadgetPermissions.Prices);
}

[DependsOn(typeof(CoworkeeWebhooksModule), typeof(CoworkeeNotificationsModule), typeof(CoworkeeIdentityModule))]
public sealed class TestWebhooksModule : CoworkeeModule
{
    public override void ConfigureServices(ModuleServiceContext context)
    {
        context.Services.AddSingleton<IModelContributor, GadgetModel>();
        context.Services.AddSingleton<IPermissionDefinitionContributor, GadgetPermissionDefinitions>();
        context.Services.AddODataEntity<Gadget>("Gadgets", GadgetPermissions.View, g => g.Cost);
        context.Services.AddScoped<IODataEntityFilter<Gadget>, GadgetRowFilter>();
        context.Services.AddCoworkeeResponseFilters([typeof(TestWebhooksModule).Assembly]);
        context.Services.AddCoworkeeWebhooks(webhooks => webhooks.Entity<Gadget>().Event("order.shipped", "An order left the warehouse"));
        context.Services.AddCoworkeeDbContext<WebhooksTestDbContext>((provider, options) =>
            options.UseCoworkeeDatabase(provider.GetRequiredService<IConfiguration>(), "test"));
    }
}
