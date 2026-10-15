using System.Net.Http.Json;
using Coworkee.AspNetCore;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Modularity;
using Coworkee.Core.Security;
using Coworkee.Domain;
using Coworkee.Identity;
using Coworkee.Identity.Setup;
using Coworkee.Infrastructure.Outbox;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Notifications;
using Coworkee.Testing;
using Coworkee.Workflows.Elsa.Activities;
using Elsa.Workflows;
using Elsa.Workflows.Activities;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

[assembly: AssemblyFixture(typeof(Coworkee.Workflows.Elsa.Tests.ElsaApp))]
[assembly: CollectionBehavior(CollectionBehavior.CollectionPerAssembly)]

namespace Coworkee.Workflows.Elsa.Tests;

/// <summary>One app for all tests and one setup: resetting the database would also empty Elsa's tables with the indexed triggers.</summary>
public sealed class ElsaApp : DatabaseFixture
{
    private SetupResultDto? _setup;

    protected override string[] SchemasToExclude => ["hangfire"];

    public WebApplication App { get; private set; } = null!;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration["Coworkee:SetupToken"] = "token";
        builder.Configuration["ConnectionStrings:test"] = ConnectionString;
        builder.Configuration["Coworkee:Database:Provider"] = Provider;
        builder.Configuration["Coworkee:Jobs:ConnectionStringName"] = "test";
        builder.Configuration["Coworkee:Jobs:RunServer"] = "false";
        builder.Configuration["Coworkee:Workflows:Elsa:StudioUrl"] = "https://studio.example/";
        builder.AddCoworkee<TestElsaModule>();
        builder.Services.AddTestAuthentication();
        App = builder.Build();
        await using (var scope = App.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<ElsaTestDbContext>().Database.EnsureCreatedAsync();
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
        if (_setup is null)
        {
            var response = await App.GetTestClient().PostAsJsonAsync("/api/v1/setup/complete",
                new CompleteSetupRequest("token", "Acme", "admin@acme.test", "Admin#12345", "Ada", "Admin"));
            response.EnsureSuccessStatusCode();
            _setup = (await response.Content.ReadFromJsonAsync<SetupResultDto>())!;
        }

        return _setup;
    }

    public HttpClient As(Guid userId, Guid tenantId) => App.GetTestClient().AsUser(userId, tenantId);

    public async Task<Guid> AddDocumentAsync(Guid tenantId)
    {
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(null, tenantId));
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ElsaTestDbContext>();
        var document = new Document { Title = "Contract", TenantId = tenantId };
        db.Add(document);
        await db.SaveChangesAsync();
        return document.Id;
    }

    /// <summary>Runs the outbox until it is empty, as the app's background service would.</summary>
    public async Task DrainOutboxAsync()
    {
        var outbox = App.Services.GetRequiredService<OutboxProcessor<ElsaTestDbContext>>();
        while (await outbox.ProcessBatchAsync(50, CancellationToken.None) > 0)
        {
        }
    }

    public async Task<List<Notification>> NotificationsAsync()
    {
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(null, null));
        await using var scope = App.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ElsaTestDbContext>().Set<Notification>().AsNoTracking().ToListAsync();
    }
}

public sealed class Document : AuditedEntity, IMultiTenant
{
    public required string Title { get; set; }

    public Guid TenantId { get; set; }
}

public sealed class ElsaTestDbContext(DbContextOptions<ElsaTestDbContext> options, ICurrentUser currentUser, IEnumerable<IModelContributor> contributors)
    : CoworkeeDbContext(options, currentUser, contributors);

internal sealed class DocumentModelContributor : IModelContributor
{
    public void Apply(ModelBuilder modelBuilder) => modelBuilder.Entity<Document>(document => document.ToTable("Documents"));
}

/// <summary>Review starts it: the reviewers hear about it, then it waits for the decision and tells the requester.</summary>
public sealed class ReviewWorkflow : WorkflowBase
{
    protected override void Build(IWorkflowBuilder builder)
    {
        var entityId = builder.WithVariable<Guid>();
        builder.Root = new Sequence
        {
            Activities =
            {
                new WorkflowStateReached { EntityType = new("Documents"), State = new("Review"), EntityId = new(entityId), CanStartWorkflow = true },
                new Notify { Role = new("Reviewer"), Title = new("A document waits for review") },
                new WaitForApproval { EntityType = new("Documents"), EntityId = new(entityId) },
                new Notify { Role = new("Reviewer"), Title = new("The review is decided") },
            },
        };
    }
}

/// <summary>A new document starts it.</summary>
public sealed class NewDocumentWorkflow : WorkflowBase
{
    protected override void Build(IWorkflowBuilder builder) => builder.Root = new Sequence
    {
        Activities =
        {
            new EntityChangedTrigger { EntityType = new("Documents"), Action = new("Created"), CanStartWorkflow = true },
            new Notify { Role = new("Reviewer"), Title = new("A new document") },
        },
    };
}

[DependsOn(typeof(CoworkeeWorkflowsElsaModule), typeof(CoworkeeNotificationsModule), typeof(CoworkeeIdentityModule))]
public sealed class TestElsaModule : CoworkeeModule
{
    public override void ConfigureServices(ModuleServiceContext context)
    {
        context.Services.AddSingleton<IModelContributor, DocumentModelContributor>();
        context.Services.AddCoworkeeWorkflows(w => w.Entity<Document>("Documents", d =>
        {
            d.Initial("Draft").Describe(x => x.Title);
            d.Transition("Submit", "Draft", "Review");
            d.State("Review").Approval("Approved", "Rejected", a => a.Role("Reviewer"));
        }));
        context.Services.AddCoworkeeElsa(e => e.Watch<Document>("Documents"));
        context.Services.AddWorkflow<ReviewWorkflow>();
        context.Services.AddWorkflow<NewDocumentWorkflow>();
        context.Services.AddCoworkeeDbContext<ElsaTestDbContext>((provider, options) =>
            options.UseCoworkeeDatabase(provider.GetRequiredService<IConfiguration>(), "test"));
        context.Services.AddSingleton<OutboxProcessor<ElsaTestDbContext>>();
    }
}
