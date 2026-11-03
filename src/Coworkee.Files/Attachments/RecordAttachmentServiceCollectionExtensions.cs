using Coworkee.Application.Retention;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Files.Attachments;

public static class RecordAttachmentServiceCollectionExtensions
{
    /// <summary>
    /// Attachments on <typeparamref name="TEntity"/>, e.g. <c>services.AddRecordAttachments&lt;LeaveRequest&gt;("LeaveRequests", a => a.Slot("certificate", 5_000_000, "application/pdf", "image/*"))</c>.
    /// Reading follows the visibility of the type's OData set; writing also needs <see cref="RecordAttachmentType.WriteRequires"/> and <see cref="RecordAttachmentType.Authorize"/> when set.
    /// </summary>
    public static IServiceCollection AddRecordAttachments<TEntity>(this IServiceCollection services, string entityType, Action<RecordAttachmentType> configure)
        where TEntity : class
    {
        Coworkee.OData.ODataEntityAccess.RequireGuidId<TEntity>("Record attachments");
        if (services.FirstOrDefault(d => d.ServiceType == typeof(RecordAttachmentOptions))?.ImplementationInstance is not RecordAttachmentOptions options)
        {
            options = new RecordAttachmentOptions();
            services.AddSingleton(options);
        }

        services.AddRetentionTarget<OrphanedAttachmentRetention>();

        var type = new RecordAttachmentType<TEntity>(entityType);
        configure(type);
        options.Types[entityType] = type;
        return services;
    }
}
