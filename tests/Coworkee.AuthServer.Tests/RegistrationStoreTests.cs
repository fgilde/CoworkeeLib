using Coworkee.Application.Registration;
using Coworkee.Contracts.Configuration;
using Coworkee.Core.Modularity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Coworkee.AuthServer.Tests;

public sealed class RegistrationStoreTests
{
    [Fact]
    public void Registration_documents_without_a_store_fail_with_a_clear_message()
    {
        var error = Should.Throw<OptionsValidationException>(() => Options(withStore: false));
        error.Message.ShouldContain("Coworkee:Registration:Documents needs an IRegistrationDocumentStore");

        Options(withStore: true).Documents.Count.ShouldBe(1);
    }

    private static RegistrationOptions Options(bool withStore)
    {
        var services = new ServiceCollection();
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
