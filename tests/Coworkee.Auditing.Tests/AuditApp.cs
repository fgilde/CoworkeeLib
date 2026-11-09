using System.Net.Http.Json;
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Auditing.History;
using Coworkee.OData;
using Coworkee.AspNetCore;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Modularity;
using Coworkee.Core.Security;
using Coworkee.Domain;
using Coworkee.Identity;
using Coworkee.Identity.Domain;
using Coworkee.Identity.Setup;
using Coworkee.Infrastructure.Auditing;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Infrastructure.Versioning;
using Coworkee.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

[assembly: AssemblyFixture(typeof(Coworkee.Auditing.Tests.AuditApp))]
[assembly: CollectionBehavior(CollectionBehavior.CollectionPerAssembly)]

namespace Coworkee.Auditing.Tests;

public sealed class AuditApp : DatabaseFixture
{
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
        builder.AddCoworkee<TestAuditModule>();
        builder.Services.AddTestAuthentication();
        App = builder.Build();
        await using (var scope = App.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<AuditTestDbContext>().Database.EnsureCreatedAsync();
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
        // values derived from settings (the request log policy) must not outlive the reset rows
        await App.Services.GetRequiredService<Microsoft.Extensions.Caching.Hybrid.HybridCache>().RemoveByTagAsync(Coworkee.Settings.ISettingProvider.CacheTag);
        App.Services.GetRequiredService<SystemStateCache>().Reset();
        var response = await App.GetTestClient().PostAsJsonAsync("/api/v1/setup/complete",
            new CompleteSetupRequest("token", "Acme", "admin@acme.test", "Admin#12345", "Ada", "Admin"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SetupResultDto>())!;
    }

    public HttpClient As(Guid userId, Guid tenantId) => App.GetTestClient().AsUser(userId, tenantId);

    public async Task<T> AsActorAsync<T>(Guid? userId, Guid? tenantId, Func<AuditTestDbContext, Task<T>> action)
    {
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(userId, tenantId));
        await using var scope = App.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<AuditTestDbContext>());
    }

    public Task<Guid> CreateNoteAsync(Guid userId, Guid tenantId, string text) => AsActorAsync(userId, tenantId, async db =>
    {
        var note = new Note { Text = text };
        db.Add(note);
        await db.SaveChangesAsync();
        return note.Id;
    });

    public Task<Guid> CreateMemoAsync(Guid userId, Guid tenantId, string text) => AsActorAsync(userId, tenantId, async db =>
    {
        var memo = new Memo { Text = text, Secret = "s1" };
        db.Add(memo);
        await db.SaveChangesAsync();
        return memo.Id;
    });

    public Task ChangeMemoAsync(Guid userId, Guid tenantId, Guid id, Action<Memo> change) => AsActorAsync(userId, tenantId, async db =>
    {
        var memo = await db.Set<Memo>().IgnoreQueryFilters([CoworkeeDbContext.SoftDeleteFilter]).SingleAsync(m => m.Id == id);
        change(memo);
        return await db.SaveChangesAsync();
    });

    public Task DeleteMemoAsync(Guid userId, Guid tenantId, Guid id) => AsActorAsync(userId, tenantId, async db =>
    {
        db.Remove(await db.Set<Memo>().SingleAsync(m => m.Id == id));
        return await db.SaveChangesAsync();
    });

    public Task<Memo> MemoAsync(Guid id) => AsActorAsync(null, null, db => db.Set<Memo>().IgnoreQueryFilters().SingleAsync(m => m.Id == id));

    public Task UpdateNoteAsync(Guid userId, Guid tenantId, Guid id, string text) => AsActorAsync(userId, tenantId, async db =>
    {
        (await db.Set<Note>().SingleAsync(n => n.Id == id)).Text = text;
        return await db.SaveChangesAsync();
    });

    /// <summary>A user of the tenant with exactly these permissions.</summary>
    public Task<Guid> CreateUserAsync(Guid tenantId, params string[] permissions) => AsActorAsync(null, null, async db =>
    {
        var email = $"user-{Guid.NewGuid():N}@acme.test";
        var user = new User { TenantId = tenantId, UserName = email, NormalizedUserName = email.ToUpperInvariant(), Email = email, NormalizedEmail = email.ToUpperInvariant() };
        db.Add(user);
        db.AddRange(permissions.Select(p => new PermissionGrant { TenantId = tenantId, Name = p, ProviderType = PermissionProviderType.User, ProviderKey = user.Id }));
        await db.SaveChangesAsync();
        return user.Id;
    });

    /// <summary>Stores a global setting; the save clears the cached settings like an administrator's would.</summary>
    public Task SetSettingAsync(string name, string? value) => AsActorAsync(null, null, async db =>
    {
        var writer = new Coworkee.Settings.SettingWriter(db, App.Services.GetRequiredService<Coworkee.Settings.ISettingDefinitionManager>(),
            App.Services.GetRequiredService<Coworkee.Settings.SettingProtector>());
        (await writer.WriteAsync(Coworkee.Contracts.Settings.SettingScope.Global, null, new Dictionary<string, string?> { [name] = value }, CancellationToken.None)).ShouldBeNull();
        return await db.SaveChangesAsync();
    });

    /// <summary>The logged request that matches, once the background writer saved it.</summary>
    public async Task<Requests.RequestLogEntry> LoggedAsync(Func<Requests.RequestLogEntry, bool> match)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var rows = await AsActorAsync(null, null, db => db.Set<Requests.RequestLogEntry>().AsNoTracking().ToListAsync());
            if (rows.FirstOrDefault(match) is { } row)
            {
                return row;
            }

            await Task.Delay(100);
        }

        throw new ShouldAssertException("The request was not logged.");
    }

    public Task<List<Requests.RequestLogEntry>> RequestsAsync() => AsActorAsync(null, null, db => db.Set<Requests.RequestLogEntry>().AsNoTracking().ToListAsync());

    public Task<List<SecurityEvent>> SecurityEventsAsync() => AsActorAsync(null, null, db => db.Set<SecurityEvent>().AsNoTracking().OrderBy(e => e.At).ToListAsync());

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

public sealed class Note : AuditedAggregateRoot, IVersioned, IMultiTenant
{
    public required string Text { get; set; }

    public Guid TenantId { get; set; }

    public int Revision { get; set; }
}

public sealed class AuditTestDbContext(DbContextOptions<AuditTestDbContext> options, ICurrentUser currentUser, IEnumerable<IModelContributor> contributors)
    : CoworkeeDbContext(options, currentUser, contributors);

internal sealed class NotesModel : IModelContributor, IVersionedTypeContributor, IPermissionDefinitionContributor
{
    public const string Manage = "Test.Notes.Manage";
    public const string Priorities = "Test.Notes.Priorities";

    public void Apply(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Note>();
        modelBuilder.Entity<Memo>();
    }

    public void Define(VersionedTypeContext context) => context.Add<Note>("Note", Manage);

    public void Define(PermissionDefinitionContext context) => context.Group("Test", "Test").Add(Manage, "Manage notes").Add(Priorities, "See priorities");
}

[DependsOn(typeof(CoworkeeAuditingModule), typeof(CoworkeeIdentityModule), typeof(CoworkeeODataModule))]
public sealed class TestAuditModule : CoworkeeModule, IWebModule
{
    public void ConfigureApplication(WebApplication app) => LogTestApi.Map(app);

    public override void ConfigureServices(ModuleServiceContext context)
    {
        context.Services.AddSingleton<NotesModel>();
        context.Services.AddSingleton<IModelContributor>(sp => sp.GetRequiredService<NotesModel>());
        context.Services.AddSingleton<IVersionedTypeContributor>(sp => sp.GetRequiredService<NotesModel>());
        context.Services.AddSingleton<IPermissionDefinitionContributor>(sp => sp.GetRequiredService<NotesModel>());
        context.Services.AddMessagingFromAssembly(typeof(TestAuditModule).Assembly);
        context.Services.AddODataEntity<Memo>("Memos", NotesModel.Manage, o => o.HideUnless(m => m.Priority, NotesModel.Priorities));
        context.Services.AddEntityHistory("Memos", NotesModel.Manage, (Memo m) => new SaveMemo(m.Id, m.Text, m.Priority));
        context.Services.AddCoworkeeDbContext<AuditTestDbContext>((provider, options) =>
            options.UseCoworkeeDatabase(provider.GetRequiredService<IConfiguration>(), "test"));
    }
}
