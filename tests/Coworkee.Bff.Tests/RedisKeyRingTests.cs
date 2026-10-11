using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.Redis;

namespace Coworkee.Bff.Tests;

/// <summary>Two web instances behind one Redis read each other's session cookies.</summary>
public sealed class RedisKeyRingTests : IAsyncLifetime
{
    private readonly RedisContainer _redis = new RedisBuilder("redis:7-alpine").Build();

    public async ValueTask InitializeAsync() => await _redis.StartAsync();

    public async ValueTask DisposeAsync() => await _redis.DisposeAsync();

    [Fact]
    public async Task What_one_instance_protects_the_other_unprotects()
    {
        await using var first = Instance();
        await using var second = Instance();

        var token = first.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("cookie").Protect("ticket");

        second.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("cookie").Unprotect(token).ShouldBe("ticket");
    }

    private WebApplication Instance()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = Directory.CreateTempSubdirectory().FullName });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Coworkee:Bff:Authority"] = "https://auth.test",
            ["Coworkee:Bff:ClientId"] = "web",
            ["Coworkee:Bff:ApiAddress"] = "http://api.test",
            ["ConnectionStrings:redis"] = _redis.GetConnectionString(),
        });
        builder.AddCoworkeeBff();
        return builder.Build();
    }
}
