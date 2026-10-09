using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
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

    public CapturingDocumentStore Documents { get; } = new();

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
            ["Coworkee:RateLimiting:Policies:auth:PermitLimit"] = "100000",
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
            ["Coworkee:Auth:External:Providers:keycloak:DisplayName"] = "Keycloak",
            ["Coworkee:Auth:External:Providers:keycloak:Authority"] = "http://keycloak.test/realms/demo",
            ["Coworkee:Auth:External:Providers:keycloak:ClientId"] = "demo-auth",
            ["Coworkee:Auth:External:Providers:keycloak:ClientSecret"] = "secret",
            ["Coworkee:Auth:External:Providers:keycloak:RequireHttpsMetadata"] = "false",
            ["Coworkee:Auth:External:Providers:trusted:Authority"] = "http://trusted.test/realms/demo",
            ["Coworkee:Auth:External:Providers:trusted:ClientId"] = "demo-auth",
            ["Coworkee:Auth:External:Providers:trusted:RequireHttpsMetadata"] = "false",
            ["Coworkee:Auth:External:Providers:trusted:TrustEmail"] = "true",
            ["Coworkee:Auth:Login:AllowUserName"] = "true",
            ["Coworkee:Auth:Login:AllowedEmails:0"] = "*@acme.test",
            ["Coworkee:Registration:RequireAddress"] = "true",
            ["Coworkee:Registration:AllowedEmails:0"] = "*@acme.test",
            ["Coworkee:Registration:RequireDocuments"] = "true",
            ["Coworkee:Registration:Documents:0:Name"] = "Passport",
            ["Coworkee:Registration:Documents:0:Names:de"] = "Reisepass",
            ["Coworkee:Registration:Documents:0:Description"] = "A scan of your passport",
            ["Coworkee:Registration:Documents:0:Descriptions:de"] = "Ein Scan Ihres Reisepasses",
            ["Coworkee:Registration:Documents:0:ContentTypes:0"] = "image/*",
            ["Coworkee:Registration:Documents:0:ContentTypes:1"] = "application/pdf",
            ["Coworkee:Registration:Documents:0:MaxSize"] = "1000",
            ["Coworkee:Registration:Documents:1:Name"] = "Certificate",
            ["Coworkee:Registration:Documents:1:Required"] = "false",
        });
        builder.AddCoworkee<TestAuthModule>();
        builder.Services.RemoveAll<Coworkee.Mailing.IMailSender>();
        builder.Services.AddSingleton<Coworkee.Mailing.IMailSender>(Mails);
        builder.Services.AddSingleton<Coworkee.Application.Registration.IRegistrationDocumentStore>(Documents);
        App = builder.Build();
        await using (var scope = App.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<AuthTestDbContext>().Database.EnsureCreatedAsync();
        }

        App.UseCoworkee();

        // stands in for the provider's callback: signs the browser into the external cookie like the OIDC handler does
        App.MapGet("/test/external", (HttpContext context, string sub, string email, string? given) => context.SignInAsync(IdentityConstants.ExternalScheme,
            new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, sub), new Claim(ClaimTypes.Email, email), new Claim("email_verified", "true"),
                .. given is null ? Array.Empty<Claim>() : [new Claim(ClaimTypes.GivenName, given)],
            ], "keycloak")),
            new AuthenticationProperties(new Dictionary<string, string?> { ["LoginProvider"] = "keycloak" })));

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
        Documents.Saved.Clear();
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

    public async Task<DateTimeOffset?> LastLoginAsync(string email)
    {
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(null, null));
        await using var scope = App.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AuthTestDbContext>().Set<Coworkee.Identity.Domain.User>()
            .Where(u => u.Email == email).Select(u => u.LastLoginAt).SingleAsync();
    }

    public Task SetSettingAsync(string name, string value) => InDbAsync(db =>
    {
        db.Add(new Coworkee.Settings.SettingValue { Name = name, Scope = Coworkee.Contracts.Settings.SettingScope.Global, Value = value });
        return db.SaveChangesAsync();
    });

    public async Task<T> InDbAsync<T>(Func<AuthTestDbContext, Task<T>> action)
    {
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(null, null));
        await using var scope = App.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<AuthTestDbContext>());
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

[DependsOn(typeof(CoworkeeAuthServerModule), typeof(Coworkee.Notifications.CoworkeeNotificationsModule))]
public sealed class TestAuthModule : CoworkeeModule
{
    public override void ConfigureServices(ModuleServiceContext context) =>
        context.Services.AddCoworkeeDbContext<AuthTestDbContext>((provider, options) =>
            options.UseNpgsql(provider.GetRequiredService<IConfiguration>().GetConnectionString("test")));
}

public sealed record CapturedMail(string To, string Template, object Model);

public sealed record CapturedDocument(Guid UserId, string Slot, string FileName, string ContentType, byte[] Content, Guid? ActingUser);

public sealed class CapturingDocumentStore : Coworkee.Application.Registration.IRegistrationDocumentStore
{
    public System.Collections.Concurrent.ConcurrentQueue<CapturedDocument> Saved { get; } = new();

    public async Task SaveAsync(Coworkee.Application.Registration.RegistrationDocument document, CancellationToken cancellationToken)
    {
        using var copy = new MemoryStream();
        await document.Content.CopyToAsync(copy, cancellationToken);
        Saved.Enqueue(new CapturedDocument(document.UserId, document.Slot.Name, document.FileName, document.ContentType, copy.ToArray(), CurrentUserScope.Current?.UserId));
    }
}

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
