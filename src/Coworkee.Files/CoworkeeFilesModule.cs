using Coworkee.Application;
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.AspNetCore;
using Coworkee.Contracts.Files;
using Coworkee.Core.Modularity;
using Coworkee.Infrastructure.Persistence;
using Coworkee.OData;
using Coworkee.Storage;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Coworkee.Files;

public sealed class FilesOptions
{
    public const string Section = "Coworkee:Files";

    /// <summary>Largest upload in bytes; also the request size limit of the upload endpoint.</summary>
    public long MaxFileSize { get; set; } = 2L * 1024 * 1024 * 1024;
}

/// <summary>Folders and files in the blob storage, with per-folder grants, soft delete, audit and realtime changes.</summary>
[DependsOn(typeof(CoworkeeApplicationModule), typeof(CoworkeeStorageModule))]
public sealed class CoworkeeFilesModule : CoworkeeModule, IWebModule
{
    public override void ConfigureServices(ModuleServiceContext context)
    {
        var services = context.Services;
        services.AddMessagingFromAssembly(typeof(CoworkeeFilesModule).Assembly);
        services.AddSingleton<IModelContributor, FileModelContributor>();
        services.AddSingleton<IPermissionDefinitionContributor, FilePermissionDefinitions>();
        services.AddScoped<IResourceHierarchy, FolderHierarchy>();
        services.AddScoped<FolderAccess>();
        services.AddScoped<Application.Privacy.IPersonalDataContributor, FilePersonalData>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddODataEntity<StoredFile>("StoredFiles", FilePermissions.View, f => f.BlobKey);
        services.AddODataEntity<FileFolder>("FileFolders", FilePermissions.View);
    }

    public void ConfigureApplication(WebApplication app) => app.MapFileEndpoints(app.Configuration.GetSection(FilesOptions.Section).Get<FilesOptions>() ?? new FilesOptions());
}

internal sealed class FilePermissionDefinitions : IPermissionDefinitionContributor
{
    public void Define(PermissionDefinitionContext context) =>
        context.Group(FilePermissions.GroupName, "Files")
            .Add(FilePermissions.View, "View files")
            .Add(FilePermissions.Upload, "Upload files", FilePermissions.View)
            .Add(FilePermissions.Manage, "Manage files", FilePermissions.Upload, FilePermissions.View);
}
