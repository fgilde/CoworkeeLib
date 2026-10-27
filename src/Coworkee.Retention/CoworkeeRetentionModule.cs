using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Application.Retention;
using Coworkee.Application.Status;
using Coworkee.AspNetCore;
using Coworkee.BackgroundJobs;
using Coworkee.Core.Modularity;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Retention.Targets;
using Coworkee.Settings;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Coworkee.Retention;

/// <summary>How long data is kept: modules report their kinds of data (<see cref="IRetentionTarget"/>), a nightly job deletes what is older than the days set, the admin page sets them.</summary>
[DependsOn(typeof(CoworkeeSettingsModule), typeof(CoworkeeBackgroundJobsModule))]
public sealed class CoworkeeRetentionModule : CoworkeeModule, IWebModule
{
    public const string Section = "Coworkee:Retention";

    public override void ConfigureServices(ModuleServiceContext context)
    {
        var services = context.Services;
        services.AddMessagingFromAssembly(typeof(CoworkeeRetentionModule).Assembly);
        services.AddSingleton<IModelContributor, RetentionModelContributor>();
        services.AddSingleton<IPermissionDefinitionContributor, RetentionPermissionDefinitions>();
        services.AddSingleton<ISettingDefinitionContributor, RetentionSettingDefinitions>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<RetentionRunner>();
        services.AddRetentionTarget<AuditRetention>().AddRetentionTarget<SnapshotRetention>().AddRetentionTarget<OutboxRetention>().AddRetentionTarget<SoftDeletedRetention>()
            .AddRetentionTarget<AccessTokenRetention>();
        services.AddSingleton<Infrastructure.Tokens.RetentionCleansAccessTokens>();
        services.AddSystemStatus<RetentionStatus>();
        services.AddRecurringJob<RetentionJob>(RetentionExtensions.JobId, context.Configuration[$"{Section}:Cron"] ?? "41 2 * * *");
    }

    public void ConfigureApplication(WebApplication app) => app.MapRetentionEndpoints();
}
