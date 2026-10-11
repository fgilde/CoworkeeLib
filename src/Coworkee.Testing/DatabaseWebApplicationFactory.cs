using Coworkee.Contracts.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Coworkee.Testing;

public class DatabaseWebApplicationFactory<TProgram>(DatabaseFixture database, string connectionStringName) : WebApplicationFactory<TProgram>
    where TProgram : class
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.UseSetting($"ConnectionStrings:{connectionStringName}", database.ConnectionString)
            .UseSetting(DatabaseOptions.ProviderKey, database.Provider);
}
