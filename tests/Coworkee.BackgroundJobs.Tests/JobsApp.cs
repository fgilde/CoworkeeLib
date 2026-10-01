using System.Collections.Concurrent;
using System.Net.Http.Json;
using Coworkee.AspNetCore;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Modularity;
using Coworkee.Core.Security;
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

[assembly: AssemblyFixture(typeof(Coworkee.BackgroundJobs.Tests.JobsApp))]
[assembly: CollectionBehavior(CollectionBehavior.CollectionPerAssembly)]

namespace Coworkee.BackgroundJobs.Tests;

public sealed class JobsApp : PostgresFixture
{
    public WebApplication App { get; private set; } = null!;

    protected override string[] SchemasToExclude => ["hangfire"];

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Coworkee:SetupToken"] = "token",
            ["ConnectionStrings:test"] = ConnectionString,
            ["Coworkee:Jobs:ConnectionStringName"] = "test",
            ["Coworkee:Jobs:PollingInterval"] = "00:00:01",
            ["Coworkee:Jobs:RetryDelaysInSeconds:0"] = "1",
        });
        builder.AddCoworkee<TestJobsModule>();
        builder.Services.AddTestAuthentication();
        App = builder.Build();
        await using (var scope = App.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<JobsTestDbContext>().Database.EnsureCreatedAsync();
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
}

public sealed class JobsTestDbContext(DbContextOptions<JobsTestDbContext> options, ICurrentUser currentUser, IEnumerable<IModelContributor> contributors)
    : CoworkeeDbContext(options, currentUser, contributors);

[DependsOn(typeof(CoworkeeBackgroundJobsModule), typeof(CoworkeeIdentityModule))]
public sealed class TestJobsModule : CoworkeeModule
{
    public override void ConfigureServices(ModuleServiceContext context) =>
        context.Services.AddCoworkeeDbContext<JobsTestDbContext>((provider, options) =>
            options.UseNpgsql(provider.GetRequiredService<IConfiguration>().GetConnectionString("test")));
}

public sealed class RecordingJob(ICurrentUser currentUser) : IBackgroundJob<string>
{
    public static ConcurrentBag<(string Value, Guid? UserId, Guid? TenantId)> Calls { get; } = [];

    public Task ExecuteAsync(string args, CancellationToken cancellationToken)
    {
        Calls.Add((args, currentUser.UserId, currentUser.TenantId));
        return Task.CompletedTask;
    }
}

public sealed class FailingOnceJob : IBackgroundJob<string>
{
    public static ConcurrentDictionary<string, int> Attempts { get; } = new();

    public Task ExecuteAsync(string args, CancellationToken cancellationToken) =>
        Attempts.AddOrUpdate(args, 1, (_, count) => count + 1) == 1
            ? throw new InvalidOperationException("first attempt fails")
            : Task.CompletedTask;
}
