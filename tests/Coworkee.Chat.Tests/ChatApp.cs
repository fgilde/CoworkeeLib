using System.Net.Http.Json;
using Coworkee.AspNetCore;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Modularity;
using Coworkee.Core.Security;
using Coworkee.Identity.Setup;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

[assembly: AssemblyFixture(typeof(Coworkee.Chat.Tests.ChatApp))]
[assembly: CollectionBehavior(CollectionBehavior.CollectionPerAssembly)]

namespace Coworkee.Chat.Tests;

public sealed class ChatApp : PostgresFixture
{
    public WebApplication App { get; private set; } = null!;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration["Coworkee:SetupToken"] = "token";
        builder.Configuration["ConnectionStrings:test"] = ConnectionString;
        builder.AddCoworkee<TestChatModule>();
        builder.Services.AddTestAuthentication();
        App = builder.Build();
        App.UseCoworkee();
        await using (var scope = App.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<ChatTestDbContext>().Database.EnsureCreatedAsync();
        }

        await App.StartAsync();
    }

    public override async ValueTask DisposeAsync()
    {
        await App.DisposeAsync();
        await base.DisposeAsync();
    }

    public async Task<SetupResultDto> SetupAsync()
    {
        await ResetAsync();
        App.Services.GetRequiredService<SystemStateCache>().Reset();
        var response = await App.GetTestClient().PostAsJsonAsync("/api/v1/setup/complete",
            new CompleteSetupRequest("token", "Acme", "admin@acme.test", "Admin#12345", "Ada", "Admin"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SetupResultDto>())!;
    }

    public HttpClient As(Guid userId, Guid tenantId) => App.GetTestClient().AsUser(userId, tenantId);
}

public sealed class ChatTestDbContext(DbContextOptions<ChatTestDbContext> options, ICurrentUser currentUser, IEnumerable<IModelContributor> contributors)
    : CoworkeeDbContext(options, currentUser, contributors);

[DependsOn(typeof(CoworkeeChatModule))]
public sealed class TestChatModule : CoworkeeModule
{
    public override void ConfigureServices(ModuleServiceContext context) =>
        context.Services.AddCoworkeeDbContext<ChatTestDbContext>((provider, options) =>
            options.UseNpgsql(provider.GetRequiredService<IConfiguration>().GetConnectionString("test")));
}
