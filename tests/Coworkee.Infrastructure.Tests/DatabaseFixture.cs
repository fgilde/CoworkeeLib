using Coworkee.Core.Security;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

[assembly: AssemblyFixture(typeof(Coworkee.Infrastructure.Tests.DatabaseFixture))]

namespace Coworkee.Infrastructure.Tests;

public sealed class DatabaseFixture : PostgresFixture
{
    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await using var provider = CreateServices(new TestCurrentUser(), new FakeTimeProvider());
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<TestDbContext>().Database.EnsureCreatedAsync();
    }

    public ServiceProvider CreateServices(ICurrentUser user, TimeProvider clock, Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(user);
        services.AddSingleton(clock);
        services.AddSingleton<IModelContributor, TestModelContributor>();
        services.AddCoworkeeDbContext<TestDbContext>((_, options) => options.UseNpgsql(ConnectionString));
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }
}
