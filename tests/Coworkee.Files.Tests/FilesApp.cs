using System.Net.Http.Json;
using Coworkee.Application.Messaging;
using Coworkee.AspNetCore;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Modularity;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Domain;
using Coworkee.Files.Attachments;
using Coworkee.Identity;
using Coworkee.Identity.Setup;
using Coworkee.Infrastructure.Persistence;
using Coworkee.OData;
using Coworkee.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

[assembly: AssemblyFixture(typeof(Coworkee.Files.Tests.FilesApp))]
[assembly: CollectionBehavior(CollectionBehavior.CollectionPerAssembly)]

namespace Coworkee.Files.Tests;

public sealed class FilesApp : DatabaseFixture
{
    private readonly string _blobs = Path.Combine(Path.GetTempPath(), "coworkee-files-tests-" + Guid.NewGuid().ToString("N"));

    public WebApplication App { get; private set; } = null!;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration["Coworkee:SetupToken"] = "token";
        builder.Configuration["Coworkee:Storage:FileSystem:Root"] = _blobs;
        builder.Configuration["ConnectionStrings:test"] = ConnectionString;
        builder.Configuration["Coworkee:Jobs:ConnectionStringName"] = "test";
        builder.Configuration["Coworkee:Jobs:RunServer"] = "false";
        builder.Configuration["Coworkee:Database:Provider"] = Provider;
        builder.AddCoworkee<TestFilesModule>();
        builder.Services.AddTestAuthentication();
        App = builder.Build();
        await using (var scope = App.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<FilesTestDbContext>().Database.EnsureCreatedAsync();
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
            Directory.Delete(_blobs, recursive: true);
        }
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

    /// <summary>The files in the blob storage, of every test so far.</summary>
    public int BlobCount() => Directory.Exists(_blobs) ? Directory.EnumerateFiles(_blobs, "*", SearchOption.AllDirectories).Count() : 0;

    public HttpClient As(Guid userId, Guid tenantId) => App.GetTestClient().AsUser(userId, tenantId);

    public async Task<Guid> AddTicketAsync(Guid userId, Guid tenantId, string title = "ticket")
    {
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(userId, tenantId));
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FilesTestDbContext>();
        var ticket = new Ticket { Title = title, TenantId = tenantId };
        db.Add(ticket);
        await db.SaveChangesAsync();
        return ticket.Id;
    }

    public async Task<T> InDbAsync<T>(Func<FilesTestDbContext, Task<T>> action)
    {
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(null, null));
        await using var scope = App.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<FilesTestDbContext>());
    }
}

public sealed class FilesTestDbContext(DbContextOptions<FilesTestDbContext> options, ICurrentUser currentUser, IEnumerable<IModelContributor> contributors)
    : CoworkeeDbContext(options, currentUser, contributors);

/// <summary>A record with attachments; private tickets are hidden by the row filter of the Tickets set from everyone but their creator.</summary>
public sealed class Ticket : AuditedEntity, IMultiTenant, ISoftDelete
{
    public const string Private = "private";

    public required string Title { get; set; }

    public Guid TenantId { get; set; }

    public bool IsDeleted { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    public Guid? DeletedBy { get; set; }
}

internal sealed class PrivateTickets(ICurrentUser currentUser) : IODataEntityFilter<Ticket>
{
    public Task<IQueryable<Ticket>> ApplyAsync(IQueryable<Ticket> query, CancellationToken cancellationToken)
    {
        var me = currentUser.UserId;
        return Task.FromResult(query.Where(t => t.Title != Ticket.Private || t.CreatedBy == me));
    }
}

/// <summary>Refuses every file with "virus" in its name, as a scanner would.</summary>
internal sealed class NameScanner : IAttachmentScanner
{
    public Task<string?> ScanAsync(AttachmentUpload upload, Stream content, CancellationToken cancellationToken) =>
        Task.FromResult(upload.FileName.Contains("virus", StringComparison.OrdinalIgnoreCase) ? "The file is infected." : null);
}

internal sealed class TicketModelContributor : IModelContributor
{
    public void Apply(ModelBuilder modelBuilder) => modelBuilder.Entity<Ticket>(ticket => ticket.ToTable("Tickets"));
}

[DependsOn(typeof(CoworkeeFilesModule), typeof(CoworkeeAttachmentsModule), typeof(CoworkeeODataModule), typeof(CoworkeeIdentityModule))]
public sealed class TestFilesModule : CoworkeeModule
{
    public override void ConfigureServices(ModuleServiceContext context)
    {
        context.Services.AddSingleton<IModelContributor, TicketModelContributor>();
        context.Services.AddODataEntity<Ticket>("Tickets");
        context.Services.AddScoped<IODataEntityFilter<Ticket>, PrivateTickets>();
        context.Services.AddSingleton<IAttachmentScanner, NameScanner>();
        context.Services.AddScoped<IHandler<AttachToTicket, Result>, AttachToTicketHandler>();
        context.Services.AddRecordAttachments<Ticket>("Tickets", a => a
            .Slot("proof", 1024, "application/pdf", "image/*")
            .MultiSlot("docs")
            .WriteRequires(Contracts.Files.FilePermissions.Upload));
        context.Services.AddCoworkeeDbContext<FilesTestDbContext>((provider, options) =>
            options.UseCoworkeeDatabase(provider.GetRequiredService<IConfiguration>(), "test"));
    }
}
