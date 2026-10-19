using System.Collections.Concurrent;
using System.Net.Http.Json;
using Coworkee.Application.Authorization;
using Coworkee.AspNetCore;
using Coworkee.BackgroundJobs;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Realtime;
using Coworkee.Core.Modularity;
using Coworkee.Core.Security;
using Coworkee.Domain;
using Coworkee.Identity;
using Coworkee.Identity.Setup;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Notifications;
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
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

[assembly: AssemblyFixture(typeof(Coworkee.Workflows.Tests.WorkflowApp))]
[assembly: CollectionBehavior(CollectionBehavior.CollectionPerAssembly)]

namespace Coworkee.Workflows.Tests;

public sealed class WorkflowApp : DatabaseFixture
{
    public const string PayPermission = "Expenses.Pay";

    protected override string[] SchemasToExclude => ["hangfire"];

    public WebApplication App { get; private set; } = null!;

    // whole seconds: the databases keep microseconds at most
    public static FakeTimeProvider Clock { get; } = new(DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds()));

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
        builder.AddCoworkee<TestWorkflowModule>();
        builder.Services.AddTestAuthentication();
        App = builder.Build();
        await using (var scope = App.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<WorkflowTestDbContext>().Database.EnsureCreatedAsync();
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

    public async Task<Guid> AddExpenseAsync(Guid tenantId, decimal amount = 100, string title = "Laptop", string? category = null)
    {
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(null, tenantId));
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkflowTestDbContext>();
        var expense = new Expense { Title = title, Amount = amount, TenantId = tenantId, Category = category is null ? null : new ExpenseCategory { Name = category } };
        db.Add(expense);
        await db.SaveChangesAsync();
        return expense.Id;
    }

    public async Task<Guid> AddTicketAsync(Guid tenantId, string title)
    {
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(null, tenantId));
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkflowTestDbContext>();
        var ticket = new Ticket { Title = title, TenantId = tenantId };
        db.Add(ticket);
        await db.SaveChangesAsync();
        return ticket.Id;
    }

    /// <summary>A workflow row written straight into a tenant, e.g. one of another tenant that names a foreign entity id.</summary>
    public async Task AddWorkflowItemAsync(Guid tenantId, string entityType, Guid entityId, string state)
    {
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(null, tenantId));
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkflowTestDbContext>();
        db.Add(new WorkflowItem { TenantId = tenantId, EntityType = entityType, EntityId = entityId, State = state, ChangedAt = Clock.GetUtcNow() });
        await db.SaveChangesAsync();
    }

    public async Task<Expense> ExpenseAsync(Guid id)
    {
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(null, null));
        await using var scope = App.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<WorkflowTestDbContext>().Set<Expense>().IgnoreQueryFilters().AsNoTracking().SingleAsync(e => e.Id == id);
    }

    public async Task<List<Notification>> NotificationsAsync()
    {
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(null, null));
        await using var scope = App.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<WorkflowTestDbContext>().Set<Notification>().ToListAsync();
    }

    public Task RunDeadlineJobAsync() => new RecurringJobRunner<WorkflowDeadlineJob>(App.Services).RunAsync(CancellationToken.None);

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

public sealed class Expense : AuditedEntity, IMultiTenant
{
    public required string Title { get; set; }

    public decimal Amount { get; set; }

    public string? Status { get; set; }

    public bool Paid { get; set; }

    public Guid TenantId { get; set; }

    public Guid? CategoryId { get; set; }

    public ExpenseCategory? Category { get; set; }
}

public sealed class ExpenseCategory : Entity
{
    public required string Name { get; set; }
}

/// <summary>A workflow without MirrorTo: its OData set reads the state from the workflow tables.</summary>
public sealed class Ticket : AuditedEntity, IMultiTenant
{
    public required string Title { get; set; }

    public Guid TenantId { get; set; }
}

public sealed class WorkflowTestDbContext(DbContextOptions<WorkflowTestDbContext> options, ICurrentUser currentUser, IEnumerable<IModelContributor> contributors)
    : CoworkeeDbContext(options, currentUser, contributors);

internal sealed class ExpenseModelContributor : IModelContributor
{
    public void Apply(ModelBuilder modelBuilder) => modelBuilder.Entity<Expense>(expense =>
    {
        expense.ToTable("Expenses");
        expense.Property(e => e.Amount).HasPrecision(18, 2);
    }).Entity<Ticket>(ticket => ticket.ToTable("Tickets")).Entity<ExpenseCategory>(category => category.ToTable("ExpenseCategories"));
}

/// <summary>A row filter of the Expenses set: secret expenses only for payers.</summary>
internal sealed class SecretExpenses(IPermissionChecker permissions) : IODataEntityFilter<Expense>
{
    public async Task<IQueryable<Expense>> ApplyAsync(IQueryable<Expense> query, CancellationToken cancellationToken) =>
        await permissions.IsGrantedAsync(WorkflowApp.PayPermission, cancellationToken) ? query : query.Where(e => !e.Title.StartsWith("Secret"));
}

internal sealed class ExpensePermissions : IPermissionDefinitionContributor
{
    public void Define(PermissionDefinitionContext context) => context.Group("Expenses", "Expenses").Add(WorkflowApp.PayPermission, "Pay expenses");
}

[DependsOn(typeof(CoworkeeWorkflowsModule), typeof(CoworkeeNotificationsModule), typeof(CoworkeeIdentityModule))]
public sealed class TestWorkflowModule : CoworkeeModule
{
    public override void ConfigureServices(ModuleServiceContext context)
    {
        context.Services.Replace(ServiceDescriptor.Singleton<TimeProvider>(WorkflowApp.Clock));
        context.Services.AddSingleton<IModelContributor, ExpenseModelContributor>();
        context.Services.AddSingleton<IPermissionDefinitionContributor, ExpensePermissions>();
        context.Services.AddCoworkeeWorkflows(w => w
            .Entity<Expense>("Expenses", e =>
            {
                e.Initial("Draft").Describe(x => x.Title).Link(id => $"/expenses/{id}").MirrorTo(x => x.Status);
                e.Transition("Submit", "Draft", "Review").When(x => x.Amount > 0);
                e.Transition("Withdraw", "Review", "Draft");
                e.Transition("Reopen", ["Approved", "Rejected"], "Draft").RequiresRole("Auditor").RequiresComment();
                e.Transition("Pay", "Approved", "Paid").RequiresPermission(WorkflowApp.PayPermission);
                e.State("Review").Approval("Approved", "Rejected", a => a.Role("Manager").All().DueIn(TimeSpan.FromDays(2)).RemindAfter(TimeSpan.FromDays(1)).EscalateTo("Auditor"));
                e.State("Paid").Set(x => x.Paid = true);
            })
            .Entity<Ticket>("Tickets", t =>
            {
                t.Initial("Open");
                t.Transition("Submit", "Open", "Review");
                t.State("Review").Approval("Done", "Rejected", a => a.Role("Manager"));
                t.State("Done").WithColor("#2e7d32");
            })
            .Entity<Expense>("Trips", e =>
            {
                e.Initial("Draft");
                e.Transition("Submit", "Draft", "Review");
                e.State("Review").Approval("Approved", "Rejected", a => a.Role("Manager").NotByRequester().DueIn(TimeSpan.FromHours(4)).OnTimeout("Expired"));
            })
            .Entity<Expense>("Claims", e =>
            {
                e.Initial("Draft");
                e.Transition("Submit", "Draft", "Review");
                e.State("Review").Approval("Approved", "Rejected", a => a.Role("Manager").NotByRequester().DueIn(TimeSpan.FromHours(4)).EscalateTo("Manager"));
            })
            .Entity<Expense>("Purchases", e =>
            {
                e.Initial("Draft").Include(x => x.Category).Describe(x => $"{x.Title} for {x.Category!.Name}");
                e.Transition("Submit", "Draft", "Review");
                e.State("Review").Approval("Approved", "Rejected", a => a.Role("Manager"));
            })
            .Entity<Expense>("Leaves", e =>
            {
                // the title holds the approver: stands in for "the manager of the requester"
                e.Initial("Draft");
                e.Transition("Submit", "Draft", "Review");
                e.State("Review").Approval("Approved", "Rejected", a => a.UsersFrom(c => Task.FromResult<IReadOnlyList<Guid>>([Guid.Parse(((Expense)c.Entity).Title)])));
            }));
        context.Services.AddODataEntity<Ticket>("Tickets");
        context.Services.AddODataEntity<Expense>("Expenses");
        context.Services.AddScoped<IODataEntityFilter<Expense>, SecretExpenses>();
        context.Services.AddCoworkeeDbContext<WorkflowTestDbContext>((provider, options) =>
            options.UseCoworkeeDatabase(provider.GetRequiredService<IConfiguration>(), "test"));
    }
}
