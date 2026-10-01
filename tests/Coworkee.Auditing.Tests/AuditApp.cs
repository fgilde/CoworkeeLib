using System.Net.Http.Json;
using Coworkee.Application.Authorization;
using Coworkee.AspNetCore;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Modularity;
using Coworkee.Core.Security;
using Coworkee.Domain;
using Coworkee.Identity;
using Coworkee.Identity.Domain;
using Coworkee.Identity.Setup;
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

public sealed class AuditApp : PostgresFixture
{
    public WebApplication App { get; private set; } = null!;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration["Coworkee:SetupToken"] = "token";
        builder.Configuration["ConnectionStrings:test"] = ConnectionString;
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

    public Task UpdateNoteAsync(Guid userId, Guid tenantId, Guid id, string text) => AsActorAsync(userId, tenantId, async db =>
    {
        (await db.Set<Note>().SingleAsync(n => n.Id == id)).Text = text;
        return await db.SaveChangesAsync();
    });

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

    public void Apply(ModelBuilder modelBuilder) => modelBuilder.Entity<Note>();

    public void Define(VersionedTypeContext context) => context.Add<Note>("Note", Manage);

    public void Define(PermissionDefinitionContext context) => context.Group("Test", "Test").Add(Manage, "Manage notes");
}

[DependsOn(typeof(CoworkeeAuditingModule), typeof(CoworkeeIdentityModule))]
public sealed class TestAuditModule : CoworkeeModule
{
    public override void ConfigureServices(ModuleServiceContext context)
    {
        context.Services.AddSingleton<NotesModel>();
        context.Services.AddSingleton<IModelContributor>(sp => sp.GetRequiredService<NotesModel>());
        context.Services.AddSingleton<IVersionedTypeContributor>(sp => sp.GetRequiredService<NotesModel>());
        context.Services.AddSingleton<IPermissionDefinitionContributor>(sp => sp.GetRequiredService<NotesModel>());
        context.Services.AddCoworkeeDbContext<AuditTestDbContext>((provider, options) =>
            options.UseNpgsql(provider.GetRequiredService<IConfiguration>().GetConnectionString("test")));
    }
}
