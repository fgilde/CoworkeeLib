using System.Net.Http.Json;
using Coworkee.Application.Authorization;
using Coworkee.AspNetCore;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Modularity;
using Coworkee.Core.Security;
using Coworkee.Domain;
using Coworkee.Identity;
using Coworkee.Identity.Setup;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

[assembly: AssemblyFixture(typeof(Coworkee.ExtendedAttributes.Tests.AttributesApp))]
[assembly: CollectionBehavior(CollectionBehavior.CollectionPerAssembly)]

namespace Coworkee.ExtendedAttributes.Tests;

public sealed class AttributesApp : PostgresFixture
{
    public const string ViewNotes = "Notes.View";
    public const string EditNotes = "Notes.Edit";

    public WebApplication App { get; private set; } = null!;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration["Coworkee:SetupToken"] = "token";
        builder.Configuration["ConnectionStrings:test"] = ConnectionString;
        builder.AddCoworkee<TestAttributesModule>();
        builder.Services.AddTestAuthentication();
        App = builder.Build();
        App.UseCoworkee();
        await using (var scope = App.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<AttributesTestDbContext>().Database.EnsureCreatedAsync();
        }

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

    public async Task<Guid> AddNoteAsync(Guid tenantId)
    {
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(null, tenantId));
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AttributesTestDbContext>();
        var note = new Note { Text = "note", TenantId = tenantId };
        db.Add(note);
        await db.SaveChangesAsync();
        return note.Id;
    }
}

public sealed class Note : Entity, IMultiTenant
{
    public required string Text { get; set; }

    public Guid TenantId { get; set; }
}

public sealed class AttributesTestDbContext(DbContextOptions<AttributesTestDbContext> options, ICurrentUser currentUser, IEnumerable<IModelContributor> contributors)
    : CoworkeeDbContext(options, currentUser, contributors);

internal sealed class NoteModelContributor : IModelContributor
{
    public void Apply(ModelBuilder modelBuilder) => modelBuilder.Entity<Note>(note => note.ToTable("Notes"));
}

internal sealed class NotePermissions : IPermissionDefinitionContributor
{
    public void Define(PermissionDefinitionContext context) =>
        context.Group("Notes", "Notes").Add(AttributesApp.ViewNotes, "View notes").Add(AttributesApp.EditNotes, "Edit notes");
}

[DependsOn(typeof(CoworkeeExtendedAttributesModule), typeof(CoworkeeIdentityModule))]
public sealed class TestAttributesModule : CoworkeeModule
{
    public override void ConfigureServices(ModuleServiceContext context)
    {
        context.Services.AddSingleton<IModelContributor, NoteModelContributor>();
        context.Services.AddSingleton<IPermissionDefinitionContributor, NotePermissions>();
        context.Services.AddExtendedAttributes<Note>("Notes", AttributesApp.ViewNotes, AttributesApp.EditNotes);
        context.Services.AddCoworkeeDbContext<AttributesTestDbContext>((provider, options) =>
            options.UseNpgsql(provider.GetRequiredService<IConfiguration>().GetConnectionString("test")));
    }
}
