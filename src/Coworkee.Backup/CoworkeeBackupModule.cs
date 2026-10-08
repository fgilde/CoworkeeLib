using Coworkee.Application;
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.AspNetCore;
using Coworkee.Core.Modularity;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Storage;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Backup;

/// <summary>Database backups as zipped Postgres COPY data in the blob storage; restored only onto the same migration.</summary>
[DependsOn(typeof(CoworkeeApplicationModule), typeof(CoworkeeStorageModule))]
public sealed class CoworkeeBackupModule : CoworkeeModule, IWebModule
{
    public override void ConfigureServices(ModuleServiceContext context)
    {
        var services = context.Services;
        services.AddMessagingFromAssembly(typeof(CoworkeeBackupModule).Assembly);
        services.AddSingleton<IModelContributor, BackupModelContributor>();
        services.AddSingleton<IPermissionDefinitionContributor, BackupPermissionDefinitions>();
        services.AddScoped<DatabaseCopy>();
    }

    public void ConfigureApplication(WebApplication app) => app.MapBackupEndpoints();
}
