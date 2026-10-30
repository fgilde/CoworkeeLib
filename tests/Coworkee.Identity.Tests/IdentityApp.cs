using System.Net.Http.Json;
using Coworkee.Application.Authorization;
using Coworkee.AspNetCore;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Modularity;
using Coworkee.Core.Security;
using Coworkee.Identity.Setup;
using Coworkee.Identity;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

[assembly: AssemblyFixture(typeof(Coworkee.Identity.Tests.IdentityApp))]
[assembly: CollectionBehavior(CollectionBehavior.CollectionPerAssembly)]

namespace Coworkee.Identity.Tests;

public sealed class IdentityApp : DatabaseFixture
{
    public const string SetupToken = "test-setup-token";

    public WebApplication App { get; private set; } = null!;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration["Coworkee:SetupToken"] = SetupToken;
        builder.Configuration[CoworkeeWebApplicationExtensions.OpenApiSetting] = "true";
        builder.Configuration["ConnectionStrings:test"] = ConnectionString;
        builder.Configuration["Coworkee:Jobs:ConnectionStringName"] = "test";
        builder.Configuration["Coworkee:Jobs:RunServer"] = "false";
        builder.Configuration["Coworkee:Database:Provider"] = Provider;
        builder.AddCoworkee<TestAppModule>();
        builder.Services.AddTestAuthentication();
        builder.Services.AddCoworkeeIdentitySeed(seed =>
        {
            seed.TenantName = "Seeded";
            seed.Roles.Add(new SeedRole("Editor", "Edits things", [IdentityPermissions.Users.View]));
            seed.Users.Add(new SeedUser("root@seed.test", "Admin#12345", "Root", null, IsAdmin: true));
            seed.Users.Add(new SeedUser("editor@seed.test", "Editor#12345", "Ed") { Roles = ["Editor"] });
            seed.Settings["Theme.Mode"] = "dark";
        });
        builder.Services.AddScoped<Coworkee.Application.Setup.ISetupStep, RecordingSetupStep>();
        builder.Services.AddScoped<Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor, RaceInjector>();
        builder.Services.AddScoped<Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor, CommandGate>();
        builder.Services.AddSingleton<Coworkee.Realtime.IRealtimePublisher>(RecordingRealtimePublisher.Instance);
        builder.Services.AddComputedPermission(ComputedPermissionTests.Permission, (c, _) => Task.FromResult(ComputedPermissionTests.Leaders.ContainsKey(c.UserId)));
        App = builder.Build();
        await using (var scope = App.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IdentityTestDbContext>().Database.EnsureCreatedAsync();
        }

        App.UseCoworkee();
        await App.StartAsync();
    }

    public override async ValueTask DisposeAsync()
    {
        await App.DisposeAsync();
        await base.DisposeAsync();
    }

    public async Task ResetAllAsync()
    {
        await ResetAsync();
        App.Services.GetRequiredService<SystemStateCache>().Reset();
    }

    public HttpClient Anonymous() => App.GetTestClient();

    public HttpClient As(Guid userId, Guid tenantId) => App.GetTestClient().AsUser(userId, tenantId);

    public async Task<SetupResultDto> SetupAsync()
    {
        var response = await Anonymous().PostAsJsonAsync("/api/v1/setup/complete",
            new CompleteSetupRequest(SetupToken, "Acme", "admin@acme.test", "Admin#12345", "Ada", "Admin"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SetupResultDto>())!;
    }

    public async Task<T> InDbAsync<T>(Guid? tenantId, Func<IdentityTestDbContext, Task<T>> action)
    {
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(null, tenantId));
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityTestDbContext>();
        var result = await action(db);
        await db.SaveChangesAsync();
        return result;
    }
}

public sealed class IdentityTestDbContext(DbContextOptions<IdentityTestDbContext> options, ICurrentUser currentUser, IEnumerable<IModelContributor> contributors)
    : CoworkeeDbContext(options, currentUser, contributors);

[DependsOn(typeof(CoworkeeIdentityModule), typeof(Coworkee.OData.CoworkeeODataModule))]
public sealed class TestAppModule : CoworkeeModule
{
    public override void ConfigureServices(ModuleServiceContext context)
    {
        context.Services.AddSingleton<Coworkee.Application.Authorization.IResourceHierarchy>(TestFolderHierarchy.Instance);
        context.Services.AddSingleton<Coworkee.Application.Authorization.IResourceRestriction>(TestFolderRestriction.Instance);
        context.Services.AddCoworkeeDbContext<IdentityTestDbContext>((provider, options) =>
            options.UseCoworkeeDatabase(provider.GetRequiredService<IConfiguration>(), "test"));
    }
}

public sealed class TestFolderRestriction : Coworkee.Application.Authorization.IResourceRestriction
{
    public static readonly TestFolderRestriction Instance = new();

    /// <summary>Resource to the role it is hidden from.</summary>
    public System.Collections.Concurrent.ConcurrentDictionary<Guid, Guid> Restricted { get; } = new();

    public string ResourceType => "Folder";

    public Task<bool> IsRestrictedAsync(Guid resourceId, IReadOnlyCollection<Guid> roleIds, CancellationToken cancellationToken) =>
        Task.FromResult(Restricted.TryGetValue(resourceId, out var role) && roleIds.Contains(role));
}

public sealed class TestFolderHierarchy : Coworkee.Application.Authorization.IResourceHierarchy
{
    public static readonly TestFolderHierarchy Instance = new();

    public System.Collections.Concurrent.ConcurrentDictionary<Guid, Guid[]> Chains { get; } = new();

    public string ResourceType => "Folder";

    public Task<IReadOnlyList<Guid>> GetInheritanceChainAsync(Guid resourceId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Guid>>(Chains.TryGetValue(resourceId, out var chain) ? chain : [resourceId]);
}

internal sealed class RecordingSetupStep : Coworkee.Application.Setup.ISetupStep
{
    public static CompleteSetupRequest? Last { get; private set; }

    public Task<Coworkee.Core.Results.Error?> ApplyAsync(CompleteSetupRequest request, Guid tenantId, CancellationToken cancellationToken)
    {
        Last = request;
        return Task.FromResult<Coworkee.Core.Results.Error?>(null);
    }
}

/// <summary>Runs <see cref="Once"/> while the next save that makes a group the default is about to write: another request changing the same rows in between.</summary>
public sealed class RaceInjector : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
{
    public static Func<Task>? Once { get; set; }

    public override async ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> SavingChangesAsync(
        Microsoft.EntityFrameworkCore.Diagnostics.DbContextEventData eventData, Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (Once is { } race && eventData.Context!.ChangeTracker.Entries<Coworkee.Identity.Domain.UserGroup>()
                .Any(e => e.State == EntityState.Modified && e.Entity.IsDefault))
        {
            Once = null;
            await race();
        }

        return result;
    }
}

/// <summary>Holds the next query that has <see cref="Held.Parameter"/> among its parameters until the test releases it: a slow load other requests wait on.</summary>
public sealed class CommandGate : Microsoft.EntityFrameworkCore.Diagnostics.DbCommandInterceptor
{
    private static Held? _next;

    public static Held Arm(object parameter) => _next = new Held(parameter);

    public override async ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<System.Data.Common.DbDataReader>> ReaderExecutingAsync(
        System.Data.Common.DbCommand command, Microsoft.EntityFrameworkCore.Diagnostics.CommandEventData eventData,
        Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<System.Data.Common.DbDataReader> result, CancellationToken cancellationToken = default)
    {
        if (_next is { } held && command.Parameters.Cast<System.Data.Common.DbParameter>().Any(p => Equals(p.Value, held.Parameter))
            && Interlocked.CompareExchange(ref _next, null, held) == held)
        {
            held.Entered.TrySetResult();
            await held.Released.Task.WaitAsync(TimeSpan.FromSeconds(30), CancellationToken.None);
        }

        return result;
    }

    public sealed class Held(object parameter)
    {
        public object Parameter { get; } = parameter;

        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Released { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
