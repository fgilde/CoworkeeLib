using Coworkee.Application.Registration;
using Coworkee.AspNetCore;
using Coworkee.Core.Modularity;
using Coworkee.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Files.Tests;

public sealed class FileRegistrationDocumentsModuleTests
{
    [Fact]
    public void An_auth_server_gets_the_document_store_and_the_file_tables_without_the_files_api()
    {
        var services = Configure(new CoworkeeFileRegistrationDocumentsModule());

        services.ShouldContain(d => d.ServiceType == typeof(IRegistrationDocumentStore) && d.ImplementationType == typeof(FileRegistrationDocuments));
        services.Count(d => d.ServiceType == typeof(IModelContributor) && d.ImplementationType == typeof(FileModelContributor)).ShouldBe(1);
        typeof(IWebModule).IsAssignableFrom(typeof(CoworkeeFileRegistrationDocumentsModule)).ShouldBeFalse("no file endpoints on the auth server");
    }

    [Fact]
    public void The_files_module_brings_the_store_along_once()
    {
        typeof(CoworkeeFilesModule).GetCustomAttributes(typeof(DependsOnAttribute), false).Cast<DependsOnAttribute>()
            .SelectMany(d => d.Modules).ShouldContain(typeof(CoworkeeFileRegistrationDocumentsModule));

        var services = Configure(new CoworkeeFileRegistrationDocumentsModule(), new CoworkeeFilesModule());

        services.Count(d => d.ServiceType == typeof(IRegistrationDocumentStore)).ShouldBe(1);
        services.Count(d => d.ServiceType == typeof(IModelContributor) && d.ImplementationType == typeof(FileModelContributor)).ShouldBe(1);
    }

    private static ServiceCollection Configure(params CoworkeeModule[] modules)
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();
        foreach (var module in modules)
        {
            module.ConfigureServices(new ModuleServiceContext(services, configuration));
        }

        return services;
    }
}
