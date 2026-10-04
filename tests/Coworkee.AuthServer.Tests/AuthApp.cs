using System.Net.Http.Json;
using Coworkee.AspNetCore;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Modularity;
using Coworkee.Core.Security;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

[assembly: AssemblyFixture(typeof(Coworkee.AuthServer.Tests.AuthApp))]
[assembly: CollectionBehavior(CollectionBehavior.CollectionPerAssembly)]

namespace Coworkee.AuthServer.Tests;

public sealed class AuthApp : PostgresFixture
{
    public const string ClientId = "test-web";
    public const string RedirectUri = "https://client.test/signin-oidc";

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
            ["ConnectionStrings:test"] = ConnectionString,
            ["Coworkee:SetupToken"] = "token",
            ["Coworkee:Auth:AllowHttp"] = "true",
            ["Coworkee:Auth:SigningCertificate:Path"] = TestCertificates.Write("signing", System.Security.Cryptography.X509Certificates.X509KeyUsageFlags.DigitalSignature),
            ["Coworkee:Auth:EncryptionCertificate:Path"] = TestCertificates.Write("encryption", System.Security.Cryptography.X509Certificates.X509KeyUsageFlags.KeyEncipherment),
            ["Coworkee:Auth:ApiScopes:test_api"] = "test_api",
            ["Coworkee:Auth:Clients:0:ClientId"] = ClientId,
            ["Coworkee:Auth:Clients:0:DisplayName"] = "Test",
            ["Coworkee:Auth:Clients:0:RedirectUris:0"] = RedirectUri,
            ["Coworkee:Auth:Clients:0:PostLogoutRedirectUris:0"] = "https://client.test/",
            ["Coworkee:Auth:Clients:0:Scopes:0"] = "test_api",
            ["Coworkee:Jobs:ConnectionStringName"] = "test",
            ["Coworkee:Jobs:RunServer"] = "false",
            ["Coworkee:Account:PublicAuthUrl"] = "http://localhost",
        });
        builder.AddCoworkee<TestAuthModule>();
        builder.Services.RemoveAll<Coworkee.Mailing.IMailSender>();
        builder.Services.AddSingleton<Coworkee.Mailing.IMailSender>(Mails);
        App = builder.Build();
        await using (var scope = App.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<AuthTestDbContext>().Database.EnsureCreatedAsync();
        }

        App.UseCoworkee();

        await App.StartAsync();
    }

    public override async ValueTask DisposeAsync()
    {
        await App.DisposeAsync();
        await base.DisposeAsync();
    }

    public HttpClient Browser()
    {
        var server = App.GetTestServer();
        return new HttpClient(new CookieContainerHandler { InnerHandler = server.CreateHandler() }) { BaseAddress = server.BaseAddress };
    }

    public async Task<SetupResultDto> SetupAsync()
    {
        await ResetAsync();
        Mails.Sent.Clear();
        await App.Services.GetRequiredService<Microsoft.Extensions.Caching.Hybrid.HybridCache>().RemoveByTagAsync("coworkee:settings");
        App.Services.GetRequiredService<Coworkee.Identity.Setup.SystemStateCache>().Reset();
        await App.Services.GetRequiredService<AuthClientSeeder>().SeedAsync(CancellationToken.None);
        var response = await Browser().PostAsJsonAsync("/api/v1/setup/complete",
            new CompleteSetupRequest("token", "Acme", "admin@acme.test", "Admin#12345", "Ada", "Admin"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SetupResultDto>())!;
    }

    public async Task<int> AuditEntriesForAsync(string entityTypePrefix)
    {
        await using var scope = App.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AuthTestDbContext>().Set<Coworkee.Infrastructure.Auditing.AuditEntry>()
            .CountAsync(e => e.EntityType.StartsWith(entityTypePrefix));
    }

    public async Task DeactivateAsync(Guid userId)
    {
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(null, null));
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthTestDbContext>();
        var user = await db.Set<Coworkee.Identity.Domain.User>().SingleAsync(u => u.Id == userId);
        user.IsActive = false;
        await db.SaveChangesAsync();
    }
}

public sealed class AuthTestDbContext(DbContextOptions<AuthTestDbContext> options, ICurrentUser currentUser, IEnumerable<IModelContributor> contributors)
    : CoworkeeDbContext(options, currentUser, contributors);

[DependsOn(typeof(CoworkeeAuthServerModule))]
public sealed class TestAuthModule : CoworkeeModule
{
    public override void ConfigureServices(ModuleServiceContext context) =>
        context.Services.AddCoworkeeDbContext<AuthTestDbContext>((provider, options) =>
            options.UseNpgsql(provider.GetRequiredService<IConfiguration>().GetConnectionString("test")));
}

public sealed record CapturedMail(string To, string Template, object Model);

public sealed class CapturingMailSender : Coworkee.Mailing.IMailSender
{
    public System.Collections.Concurrent.ConcurrentQueue<CapturedMail> Sent { get; } = new();

    public Task<Guid> QueueAsync(string to, string template, object model, string? culture, CancellationToken cancellationToken)
    {
        Sent.Enqueue(new CapturedMail(to, template, model));
        return Task.FromResult(Guid.CreateVersion7());
    }

    public string LinkFor(string template, string property)
    {
        var mail = Sent.Last(m => m.Template == template);
        return (string)mail.Model.GetType().GetProperty(property)!.GetValue(mail.Model)!;
    }
}
