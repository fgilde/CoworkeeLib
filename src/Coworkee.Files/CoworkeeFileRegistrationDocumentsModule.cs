using Coworkee.Core.Modularity;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Coworkee.Files;

/// <summary>
/// Keeps the documents of the registration in the files (Registrations/{user}), without the files api: the module an auth server depends on.
/// <see cref="CoworkeeFilesModule"/> brings it along; an own <see cref="Application.Registration.IRegistrationDocumentStore"/> registered later wins.
/// </summary>
[DependsOn(typeof(CoworkeeStorageModule))]
public sealed class CoworkeeFileRegistrationDocumentsModule : CoworkeeModule
{
    public override void ConfigureServices(ModuleServiceContext context)
    {
        var services = context.Services;
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelContributor, FileModelContributor>());
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<Application.Registration.IRegistrationDocumentStore, FileRegistrationDocuments>();
        // found by its name: a renamed one would be created again with the next registration
        services.Configure<Identity.Users.IdentityAdministrationOptions>(o => o.FixedRoleNames[FileRegistrationDocuments.ReaderRole] = "Files: registration documents");
    }
}
