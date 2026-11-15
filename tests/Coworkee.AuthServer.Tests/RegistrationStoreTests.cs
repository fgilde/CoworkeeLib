using Coworkee.Application.Registration;
using Coworkee.Contracts.Configuration;
using Coworkee.Core.Modularity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Coworkee.AuthServer.Tests;

public sealed class RegistrationStoreTests
{
    [Fact]
    public void Registration_documents_without_a_store_are_skipped_instead_of_stopping_the_server()
    {
        // an admin turns the documents on in the app while the server runs; that must never take the sign-in down
        Should.NotThrow(() => Options(withStore: false)).RequireDocuments.ShouldBeFalse();

        var stored = Options(withStore: true);
        stored.RequireDocuments.ShouldBeTrue();
        stored.Documents.Count.ShouldBe(1);
    }

    private static RegistrationOptions Options(bool withStore)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        new CoworkeeAuthServerModule().ConfigureServices(new ModuleServiceContext(services, new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Coworkee:Auth:DevelopmentCertificates"] = "true",
            ["Coworkee:Registration:RequireDocuments"] = "true",
            ["Coworkee:Registration:Documents:0:Name"] = "Passport",
        }).Build()));
        if (withStore)
        {
            services.AddSingleton(NSubstitute.Substitute.For<IRegistrationDocumentStore>());
        }

        return services.BuildServiceProvider().GetRequiredService<IOptions<RegistrationOptions>>().Value;
    }
}
