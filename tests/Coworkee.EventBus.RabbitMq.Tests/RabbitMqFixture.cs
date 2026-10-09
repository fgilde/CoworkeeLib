using Coworkee.Application.Messaging;
using Coworkee.Core.Modularity;
using Coworkee.Core.Security;
using Coworkee.Infrastructure.Outbox;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.RabbitMq;

[assembly: AssemblyFixture(typeof(Coworkee.EventBus.RabbitMq.Tests.RabbitMqFixture))]
[assembly: CollectionBehavior(CollectionBehavior.CollectionPerAssembly)]

namespace Coworkee.EventBus.RabbitMq.Tests;

public sealed class RabbitMqFixture : PostgresFixture
{
    private readonly RabbitMqContainer _rabbit = new RabbitMqBuilder("rabbitmq:4-alpine").Build();

    public string RabbitConnectionString => _rabbit.GetConnectionString();

    public override async ValueTask InitializeAsync()
    {
        await Task.WhenAll(base.InitializeAsync().AsTask(), _rabbit.StartAsync());
        await using var provider = CreateServices("setup");
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<RabbitDbContext>().Database.EnsureCreatedAsync();
    }

    public override async ValueTask DisposeAsync()
    {
        await _rabbit.DisposeAsync();
        await base.DisposeAsync();
    }

    public ServiceProvider CreateServices(string queue)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{RabbitMqOptions.Section}:ConnectionString"] = RabbitConnectionString,
            [$"{RabbitMqOptions.Section}:Queue"] = queue,
            [$"{RabbitMqOptions.Section}:MaxDeliveries"] = "2",
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ICurrentUser>(new TestCurrentUser());
        services.AddSingleton<Recorder>();
        services.AddCoworkeeDbContext<RabbitDbContext>((_, options) => options.UseNpgsql(ConnectionString));
        services.AddCoworkeeOutboxProcessing<RabbitDbContext>();
        new CoworkeeEventBusModule().ConfigureServices(new ModuleServiceContext(services, configuration));
        new CoworkeeRabbitMqEventBusModule().ConfigureServices(new ModuleServiceContext(services, configuration));
        services.AddMessagingFromAssembly(typeof(RabbitMqFixture).Assembly);
        return services.BuildServiceProvider();
    }
}

public sealed class RabbitDbContext(DbContextOptions<RabbitDbContext> options, ICurrentUser currentUser, IEnumerable<IModelContributor> contributors)
    : CoworkeeDbContext(options, currentUser, contributors);
