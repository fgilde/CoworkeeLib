using System.Net.Http.Json;
using Coworkee.AspNetCore;
using Coworkee.BackgroundJobs;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Security;
using Coworkee.Identity.Setup;
using Coworkee.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

[assembly: AssemblyFixture(typeof(Coworkee.Retention.Tests.RetentionApp))]
[assembly: CollectionBehavior(CollectionBehavior.CollectionPerAssembly)]

namespace Coworkee.Retention.Tests;

/// <summary>An app with every module that brings a retention target, a fake clock and the jobs the page starts recorded for the test to run.</summary>
public sealed class RetentionApp : DatabaseFixture
{
    private readonly string _blobs = Path.Combine(Path.GetTempPath(), "coworkee-retention-tests", Guid.NewGuid().ToString("N"));

    protected override string[] SchemasToExclude => ["hangfire"];

    public WebApplication App { get; private set; } = null!;

    // whole seconds: the database keeps microseconds only
    public FakeTimeProvider Clock { get; } = new(DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds()));

    public RecordedJobs Jobs { get; } = new();

    public string BlobRoot => _blobs;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration["Coworkee:SetupToken"] = "token";
        builder.Configuration["Coworkee:Storage:FileSystem:Root"] = _blobs;
        builder.Configuration["ConnectionStrings:test"] = ConnectionString;
        builder.Configuration["Coworkee:Database:Provider"] = Provider;
        builder.Configuration["Coworkee:Jobs:ConnectionStringName"] = "test";
        builder.Configuration["Coworkee:Jobs:RunServer"] = "false";
        builder.AddCoworkee<TestRetentionModule>();
        builder.Services.AddTestAuthentication();
        builder.Services.AddSingleton<TimeProvider>(Clock);
        builder.Services.AddSingleton(Jobs);
        builder.Services.AddScoped<IBackgroundJobs, RecordingJobs>();
        App = builder.Build();
        await using (var scope = App.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<RetentionTestDbContext>().Database.EnsureCreatedAsync();
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
        Jobs.Clear();
        var response = await App.GetTestClient().PostAsJsonAsync("/api/v1/setup/complete",
            new CompleteSetupRequest("token", "Acme", "admin@acme.test", "Admin#12345", "Ada", "Admin"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SetupResultDto>())!;
    }

    public HttpClient As(Guid userId, Guid tenantId) => App.GetTestClient().AsUser(userId, tenantId);

    public async Task<T> InDbAsync<T>(Guid? tenantId, Func<RetentionTestDbContext, Task<T>> action)
    {
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(null, tenantId));
        await using var scope = App.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<RetentionTestDbContext>());
    }

    public async Task InScopeAsync(Func<IServiceProvider, Task> action)
    {
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(null, null));
        await using var scope = App.Services.CreateAsyncScope();
        await action(scope.ServiceProvider);
    }

    /// <summary>Runs the jobs the page queued, as the user who queued them.</summary>
    public async Task RunQueuedJobsAsync()
    {
        foreach (var job in Jobs.Take())
        {
            await new JobRunner<RetentionRunJob, RetentionRunArgs>(App.Services).RunAsync(job, CancellationToken.None);
        }
    }

    /// <summary>The nightly job, outside of any user like the recurring job runner.</summary>
    public Task RunNightlyAsync() => new RecurringJobRunner<RetentionJob>(App.Services).RunAsync(CancellationToken.None);
}
