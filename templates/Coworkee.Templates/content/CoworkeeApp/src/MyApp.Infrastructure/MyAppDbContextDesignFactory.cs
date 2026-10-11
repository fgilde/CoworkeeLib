using Coworkee.Contracts.Configuration;
using Coworkee.Core.Modularity;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MyApp.Infrastructure;

public sealed class MyAppDbContextDesignFactory : IDesignTimeDbContextFactory<MyAppDbContext>
{
    public MyAppDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection([new($"ConnectionStrings:{MyAppInfrastructureModule.ConnectionStringName}", DesignConnectionString),
                new(DatabaseOptions.ProviderKey, MyAppInfrastructureModule.DatabaseProvider)])
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddCoworkeeModules<MyAppDatabaseModule>(configuration);
        return services.BuildServiceProvider().CreateScope().ServiceProvider.GetRequiredService<MyAppDbContext>();
    }

    // never opened: migrations are made from the model
#if (database == "sqlServer")
    private const string DesignConnectionString = "Server=localhost;Database=myapp_design;Trusted_Connection=True;TrustServerCertificate=True";
#elif (database == "sqlite")
    private const string DesignConnectionString = "Data Source=myapp_design.db";
#else
    private const string DesignConnectionString = "Host=localhost;Database=myapp_design";
#endif
}
