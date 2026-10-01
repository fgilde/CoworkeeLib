using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Coworkee.Testing;

public class PostgresWebApplicationFactory<TProgram>(PostgresFixture postgres, string connectionStringName) : WebApplicationFactory<TProgram>
    where TProgram : class
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.UseSetting($"ConnectionStrings:{connectionStringName}", postgres.ConnectionString);
}
