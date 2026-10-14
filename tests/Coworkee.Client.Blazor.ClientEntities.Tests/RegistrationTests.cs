using Coworkee.Client.Blazor.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using NSubstitute;

namespace Coworkee.Client.Blazor.ClientEntities.Tests;

public sealed class RegistrationTests
{
    // the HttpClientFactory resolves handlers in a scope of their own and disposes it synchronously
    [Fact]
    public void A_handler_scope_disposes_synchronously()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IJSRuntime>());
        services.AddSingleton(Substitute.For<IODataClient>());
        services.AddSingleton(new CoworkeeClientOptions());
        services.AddCoworkeeClientEntities();
        using var provider = services.BuildServiceProvider();

        var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<ClientEntityHttpHandler>();

        Should.NotThrow(scope.Dispose);
    }
}
