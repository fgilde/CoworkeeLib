using Coworkee.Application;
using Coworkee.AspNetCore;
using Coworkee.Core.Modularity;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Storage;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Coworkee.Files.Attachments;

/// <summary>Files of records in named slots (a leave request's certificate, an applicant's CV), guarded by the record's visibility; types opt in with AddRecordAttachments.</summary>
[DependsOn(typeof(CoworkeeApplicationModule), typeof(CoworkeeStorageModule))]
public sealed class CoworkeeAttachmentsModule : CoworkeeModule, IWebModule
{
    public override void ConfigureServices(ModuleServiceContext context)
    {
        var services = context.Services;
        if (!services.Any(d => d.ServiceType == typeof(RecordAttachmentOptions)))
        {
            services.AddSingleton(new RecordAttachmentOptions());
        }

        services.AddSingleton<IModelContributor, RecordAttachmentModelContributor>();
        services.AddScoped<IRecordAttachments, RecordAttachments>();
        services.AddScoped<AttachmentGuard>();
        services.TryAddSingleton(TimeProvider.System);
    }

    public void ConfigureApplication(WebApplication app) => app.MapAttachmentEndpoints(app.Services.GetRequiredService<RecordAttachmentOptions>());
}
