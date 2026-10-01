using System.Collections.Concurrent;
using System.Net.Http.Json;
using Coworkee.AspNetCore;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Modularity;
using Coworkee.Core.Security;
using Coworkee.Identity.Setup;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Mailing;
using Coworkee.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

[assembly: AssemblyFixture(typeof(Coworkee.Account.Tests.AccountApp))]
[assembly: CollectionBehavior(CollectionBehavior.CollectionPerAssembly)]

namespace Coworkee.Account.Tests;

public sealed class AccountApp : PostgresFixture
{
    public WebApplication App { get; private set; } = null!;

    public CapturingMailSender Mails { get; } = new();

    protected override string[] SchemasToExclude => ["hangfire"];

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Coworkee:SetupToken"] = "token",
            ["ConnectionStrings:test"] = ConnectionString,
            ["Coworkee:Jobs:ConnectionStringName"] = "test",
            ["Coworkee:Jobs:RunServer"] = "false",
            ["Coworkee:Account:PublicAuthUrl"] = "https://auth.test",
        });
        builder.AddCoworkee<TestAccountModule>();
        builder.Services.AddTestAuthentication();
        builder.Services.RemoveAll<IMailSender>();
        builder.Services.AddSingleton<IMailSender>(Mails);
        App = builder.Build();
        await using (var scope = App.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<AccountTestDbContext>().Database.EnsureCreatedAsync();
        }

        App.UseCoworkee();
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
        Mails.Sent.Clear();
        App.Services.GetRequiredService<SystemStateCache>().Reset();
        var response = await App.GetTestClient().PostAsJsonAsync("/api/v1/setup/complete",
            new CompleteSetupRequest("token", "Acme", "admin@acme.test", "Admin#12345", "Ada", "Admin"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SetupResultDto>())!;
    }

    public HttpClient As(Guid userId, Guid tenantId) => App.GetTestClient().AsUser(userId, tenantId);

    public async Task<T> InScopeAsync<T>(Guid? userId, Guid? tenantId, Func<IServiceProvider, Task<T>> action)
    {
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(userId, tenantId));
        await using var scope = App.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }
}

public sealed record CapturedMail(string To, string Template, object Model);

public sealed class CapturingMailSender : IMailSender
{
    public ConcurrentQueue<CapturedMail> Sent { get; } = new();

    public Task<Guid> QueueAsync(string to, string template, object model, string? culture, CancellationToken cancellationToken)
    {
        Sent.Enqueue(new CapturedMail(to, template, model));
        return Task.FromResult(Guid.CreateVersion7());
    }

    public static string Link(CapturedMail mail, string property) => (string)mail.Model.GetType().GetProperty(property)!.GetValue(mail.Model)!;
}

public sealed class AccountTestDbContext(DbContextOptions<AccountTestDbContext> options, ICurrentUser currentUser, IEnumerable<IModelContributor> contributors)
    : CoworkeeDbContext(options, currentUser, contributors);

[DependsOn(typeof(CoworkeeAccountModule))]
public sealed class TestAccountModule : CoworkeeModule
{
    public override void ConfigureServices(ModuleServiceContext context) =>
        context.Services.AddCoworkeeDbContext<AccountTestDbContext>((provider, options) =>
            options.UseNpgsql(provider.GetRequiredService<IConfiguration>().GetConnectionString("test")));
}
