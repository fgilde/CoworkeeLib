using Coworkee.Application.Messaging;
using Coworkee.Core.Modularity;
using Coworkee.Core.Security;
using Coworkee.Infrastructure.Outbox;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

[assembly: AssemblyFixture(typeof(Coworkee.EventBus.Tests.EventBusFixture))]
[assembly: CollectionBehavior(CollectionBehavior.CollectionPerAssembly)]

namespace Coworkee.EventBus.Tests;

public sealed class EventBusFixture : PostgresFixture
{
    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await using var provider = CreateServices(TimeProvider.System);
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<EventBusDbContext>().Database.EnsureCreatedAsync();
    }

    public ServiceProvider CreateServices(TimeProvider clock, Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(clock);
        services.AddSingleton<ICurrentUser>(new TestCurrentUser());
        services.AddSingleton<Recorder>();
        services.AddCoworkeeDbContext<EventBusDbContext>((_, options) => options.UseNpgsql(ConnectionString));
        services.AddCoworkeeOutboxProcessing<EventBusDbContext>();
        new CoworkeeEventBusModule().ConfigureServices(new ModuleServiceContext(services, new ConfigurationBuilder().Build()));
        services.AddMessagingFromAssembly(typeof(EventBusFixture).Assembly);
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }
}

public sealed class EventBusDbContext(DbContextOptions<EventBusDbContext> options, ICurrentUser currentUser, IEnumerable<IModelContributor> contributors)
    : CoworkeeDbContext(options, currentUser, contributors);
