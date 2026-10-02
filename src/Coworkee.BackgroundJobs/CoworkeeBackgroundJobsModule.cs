using Coworkee.Application;
using Coworkee.Application.Authorization;
using Coworkee.AspNetCore;
using Coworkee.Contracts.Jobs;
using Coworkee.Core.Modularity;
using Hangfire;
using Hangfire.Dashboard;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Coworkee.BackgroundJobs;

public sealed class BackgroundJobOptions
{
    public const string Section = "Coworkee:Jobs";

    public string ConnectionStringName { get; set; } = "default";

    public bool RunServer { get; set; } = true;

    public string[] Queues { get; set; } = ["default", "mail"];

    public int WorkerCount { get; set; } = 5;

    public int Attempts { get; set; } = 3;

    public int[] RetryDelaysInSeconds { get; set; } = [10, 60, 300];

    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(15);

    public string DashboardPath { get; set; } = "/admin/jobs";
}

[DependsOn(typeof(CoworkeeApplicationModule))]
public sealed class CoworkeeBackgroundJobsModule : CoworkeeModule, IWebModule
{
    public override void ConfigureServices(ModuleServiceContext context)
    {
        var services = context.Services;
        var runServer = context.Configuration.GetSection(BackgroundJobOptions.Section).Get<BackgroundJobOptions>()?.RunServer ?? true;
        services.Configure<BackgroundJobOptions>(context.Configuration.GetSection(BackgroundJobOptions.Section));
        services.AddSingleton<IPermissionDefinitionContributor, JobsPermissionDefinitions>();
        services.AddScoped<IBackgroundJobs, HangfireBackgroundJobs>();

        services.AddHangfire((provider, config) =>
        {
            var options = provider.GetRequiredService<IOptions<BackgroundJobOptions>>().Value;
            var connectionString = provider.GetRequiredService<IConfiguration>().GetConnectionString(options.ConnectionStringName)
                ?? throw new InvalidOperationException($"Connection string '{options.ConnectionStringName}' for background jobs is missing.");
            config.UseSimpleAssemblyNameTypeSerializer()
                .UseRecommendedSerializerSettings()
                .UsePostgreSqlStorage(
                    storage => storage.UseNpgsqlConnection(connectionString),
                    new PostgreSqlStorageOptions { SchemaName = "hangfire", QueuePollInterval = options.PollingInterval })
                .UseFilter(new AutomaticRetryAttribute { Attempts = Math.Max(0, options.Attempts - 1), DelaysInSeconds = options.RetryDelaysInSeconds });
        });
        GlobalJobFilters.Filters.Remove(typeof(AutomaticRetryAttribute));

        if (runServer)
        {
            services.AddHangfireServer((provider, server) =>
            {
                var options = provider.GetRequiredService<IOptions<BackgroundJobOptions>>().Value;
                server.Queues = options.Queues;
                server.WorkerCount = options.WorkerCount;
                server.SchedulePollingInterval = options.PollingInterval;
            });
        }
    }

    public void ConfigureApplication(WebApplication app)
    {
        app.UseHangfireDashboard(
            app.Services.GetRequiredService<IOptions<BackgroundJobOptions>>().Value.DashboardPath,
            new DashboardOptions { AsyncAuthorization = [new PermissionDashboardAuthorization()], DisplayStorageConnectionString = false });
        var manager = app.Services.GetRequiredService<IRecurringJobManager>();
        foreach (var job in app.Services.GetServices<RecurringJobRegistration>())
        {
            job.Schedule(manager, job.Id, job.Cron);
        }
    }
}

internal sealed class JobsPermissionDefinitions : IPermissionDefinitionContributor
{
    public void Define(PermissionDefinitionContext context) =>
        context.Group(JobsPermissions.GroupName, "Background jobs").Add(JobsPermissions.View, "View background jobs");
}

internal sealed class PermissionDashboardAuthorization : IDashboardAsyncAuthorizationFilter
{
    public async Task<bool> AuthorizeAsync(DashboardContext context)
    {
        var httpContext = context.GetHttpContext();
        var result = await httpContext.AuthenticateAsync();
        if (result.Succeeded)
        {
            httpContext.User = result.Principal;
        }

        return result.Succeeded
            && await httpContext.RequestServices.GetRequiredService<IPermissionChecker>().IsGrantedAsync(JobsPermissions.View, httpContext.RequestAborted);
    }
}
