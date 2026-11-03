using Coworkee.Application.Authorization;
using Coworkee.Application.Retention;
using Coworkee.BackgroundJobs;
using Coworkee.Infrastructure.Persistence;
using Coworkee.OData.Transfer.Import;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.OData.Transfer;

internal static class DataTransferServiceCollectionExtensions
{
    public static IServiceCollection AddDataTransfer(this IServiceCollection services)
    {
        services.AddSingleton<IModelContributor, DataTransferModelContributor>();
        services.AddSingleton<IPermissionDefinitionContributor, DataTransferPermissionDefinitions>();
        services.AddScoped<ImportService>();
        services.AddScoped<ImportProfiles>();
        services.AddScoped<DataTransferJobs>();
        services.AddScoped<DataExports>();
        services.AddSingleton<ExportCancellations>();
        services.AddScoped<ImportJob>();
        services.AddScoped<ExportJob>();
        services.AddScoped<ExportScheduleRunner>();
        services.AddRecurringJob<ExportScheduleJob>(ExportScheduleJob.Id, ExportScheduleJob.Cron);
        services.AddRecurringJob<StaleExportJob>(StaleExportJob.Id, StaleExportJob.Cron);
        services.AddRetentionTarget<DataTransferJobRetention>();
        services.AddScoped<Application.Privacy.IPersonalDataContributor, DataTransferPersonalData>();
        return services;
    }
}
