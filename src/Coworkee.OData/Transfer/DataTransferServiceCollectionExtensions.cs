using Coworkee.Application.Authorization;
using Coworkee.Application.Retention;
using Coworkee.Contracts.Data;
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
        services.AddScoped<ImportJob>();
        services.AddScoped<ExportJob>();
        services.AddRetentionTarget<DataTransferJobRetention>();
        return services;
    }
}

internal sealed class DataTransferPermissionDefinitions : IPermissionDefinitionContributor
{
    public void Define(PermissionDefinitionContext context) =>
        context.Group(DataTransferPermissions.GroupName, "Import and export").Add(DataTransferPermissions.Use, "Open the import and export page");
}
