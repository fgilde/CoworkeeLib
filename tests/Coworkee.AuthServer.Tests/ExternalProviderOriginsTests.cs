using Coworkee.AuthServer.External;
using Coworkee.Contracts.Configuration;

namespace Coworkee.AuthServer.Tests;

public sealed class ExternalProviderOriginsTests
{
    [Fact]
    public void The_sign_in_form_may_redirect_to_the_provider_address_of_the_browser_too()
    {
        var options = new ExternalLoginOptions();
        options.Providers["keycloak"] = new() { Authority = "http://keycloak:8080/realms/shop", BrowserUrl = "https://login.example.com" };

        ExternalProviders.Origins(options).ShouldBe(["http://keycloak:8080", "https://login.example.com"]);
    }
}
