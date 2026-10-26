using Coworkee.Client.Blazor.Data;
using Microsoft.AspNetCore.Components;
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

    // a new group selection drops the local copies through the client's cache hook
    [Fact]
    public async Task The_local_copies_are_the_clients_data_cache()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<IJSRuntime>());
        services.AddSingleton(Substitute.For<IODataClient>());
        services.AddSingleton(new CoworkeeClientOptions());
        services.AddSingleton<NavigationManager, TestNavigation>();
        services.AddCoworkeeClientEntities();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        scope.ServiceProvider.GetRequiredService<IClientDataCache>().ShouldBeSameAs(scope.ServiceProvider.GetRequiredService<ClientEntities>());
    }

    private sealed class TestNavigation : NavigationManager
    {
        public TestNavigation() => Initialize("http://localhost/", "http://localhost/");
    }
}
