using System.Net.Http.Json;
using System.Text.Json;
using Coworkee.Application.Messaging;
using Coworkee.AspNetCore;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Modularity;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Identity;
using Coworkee.Identity.Setup;
using Coworkee.Infrastructure.Outbox;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Testing;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

[assembly: AssemblyFixture(typeof(Coworkee.Mailing.Tests.MailApp))]
[assembly: CollectionBehavior(CollectionBehavior.CollectionPerAssembly)]

namespace Coworkee.Mailing.Tests;

public sealed class MailApp : DatabaseFixture
{
    private readonly IContainer _mailpit = new ContainerBuilder("axllent/mailpit:v1.27")
        .WithPortBinding(1025, true)
        .WithPortBinding(8025, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(8025).ForPath("/api/v1/info")))
        .Build();

    public WebApplication App { get; private set; } = null!;

    public HttpClient Mailpit { get; private set; } = null!;

    /// <summary>Runs once after the next recipient page was read, then clears itself.</summary>
    public Func<Task>? AfterPage { get; set; }

    protected override string[] SchemasToExclude => ["hangfire"];

    public override async ValueTask InitializeAsync()
    {
        await Task.WhenAll(base.InitializeAsync().AsTask(), _mailpit.StartAsync());
        Mailpit = new HttpClient { BaseAddress = new Uri($"http://{_mailpit.Hostname}:{_mailpit.GetMappedPublicPort(8025)}") };
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Coworkee:SetupToken"] = "token",
            ["ConnectionStrings:test"] = ConnectionString,
            ["Coworkee:Database:Provider"] = Provider,
            ["Coworkee:Jobs:ConnectionStringName"] = "test",
            ["Coworkee:Jobs:PollingInterval"] = "00:00:01",
            ["Coworkee:Jobs:RetryDelaysInSeconds:0"] = "1",
            ["Coworkee:Jobs:RetryDelaysInSeconds:1"] = "1",
            ["Coworkee:Jobs:RetryDelaysInSeconds:2"] = "1",
            ["Coworkee:Settings:Defaults:Mail.Smtp.Host"] = _mailpit.Hostname,
            ["Coworkee:Settings:Defaults:Mail.Smtp.Port"] = _mailpit.GetMappedPublicPort(1025).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Coworkee:Settings:Defaults:Mail.From"] = "noreply@coworkee.test",
        });
        builder.AddCoworkee<TestMailModule>();
        var directory = builder.Services.Single(d => d.ServiceType == typeof(IMailRecipientDirectory));
        builder.Services.Remove(directory);
        builder.Services.AddScoped<IMailRecipientDirectory>(sp => new HookedRecipientDirectory(
            (IMailRecipientDirectory)ActivatorUtilities.CreateInstance(sp, directory.ImplementationType!), () => { var action = AfterPage; AfterPage = null; return action; }));
        builder.Services.AddTestAuthentication();
        App = builder.Build();
        await using (var scope = App.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<MailTestDbContext>().Database.EnsureCreatedAsync();
        }

        App.UseCoworkee();
        await App.StartAsync();
    }

    public override async ValueTask DisposeAsync()
    {
        await App.DisposeAsync();
        await _mailpit.DisposeAsync();
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

    public async Task<TResult> AsActorAsync<TResult>(Guid? userId, Guid? tenantId, Func<IServiceProvider, Task<TResult>> action)
    {
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(userId, tenantId));
        await using var scope = App.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }

    public Task<T> InDbAsync<T>(Func<MailTestDbContext, Task<T>> action) =>
        AsActorAsync(null, null, sp => action(sp.GetRequiredService<MailTestDbContext>()));

    public Task<(Guid UserId, Guid TenantId)> CreateTenantAdminAsync() => InDbAsync(async db =>
    {
        var tenant = new Coworkee.Identity.Domain.Tenant { Name = "Other", Identifier = "other-" + Guid.NewGuid().ToString("N")[..8] };
        var email = $"admin-{Guid.NewGuid():N}@other.test";
        var user = new Coworkee.Identity.Domain.User { TenantId = tenant.Id, UserName = email, NormalizedUserName = email.ToUpperInvariant(), Email = email, NormalizedEmail = email.ToUpperInvariant() };
        var adminRole = await db.Set<Coworkee.Identity.Domain.Role>().Where(r => r.IsSystem && r.Name == Coworkee.Identity.Domain.SystemRoles.Admin).Select(r => r.Id).SingleAsync();
        db.Add(tenant);
        db.Add(user);
        db.Add(new Microsoft.AspNetCore.Identity.IdentityUserRole<Guid> { UserId = user.Id, RoleId = adminRole });
        await db.SaveChangesAsync();
        return (user.Id, tenant.Id);
    });

    public async Task<IReadOnlyList<(string To, string Subject)>> MessagesToAsync(string address)
    {
        var json = await Mailpit.GetFromJsonAsync<JsonElement>("/api/v1/messages");
        return json.GetProperty("messages").EnumerateArray()
            .Select(m => (To: m.GetProperty("To")[0].GetProperty("Address").GetString()!, Subject: m.GetProperty("Subject").GetString()!))
            .Where(m => string.Equals(m.To, address, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public static async Task Eventually(Func<Task<bool>> condition, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(30));
        while (!await condition())
        {
            DateTime.UtcNow.ShouldBeLessThan(deadline, "condition not met in time");
            await Task.Delay(250, TestContext.Current.CancellationToken);
        }
    }
}

public sealed class MailTestDbContext(DbContextOptions<MailTestDbContext> options, ICurrentUser currentUser, IEnumerable<IModelContributor> contributors)
    : CoworkeeDbContext(options, currentUser, contributors);

[DependsOn(typeof(CoworkeeMailingModule), typeof(CoworkeeIdentityModule), typeof(Coworkee.Account.CoworkeeAccountModule))]
public sealed class TestMailModule : CoworkeeModule
{
    public override void ConfigureServices(ModuleServiceContext context)
    {
        context.Services.AddCoworkeeDbContext<MailTestDbContext>((provider, options) =>
            options.UseCoworkeeDatabase(provider.GetRequiredService<IConfiguration>(), "test"));
        context.Services.AddCoworkeeOutboxProcessing<MailTestDbContext>();
        context.Services.AddMessagingFromAssembly(typeof(TestMailModule).Assembly);
        context.Services.AddSingleton<IMailTemplateContributor, TestTemplates>();
    }
}

internal sealed class TestTemplates : IMailTemplateContributor
{
    public void Define(MailTemplateContext context) =>
        context.Add(new MailTemplateDefinition("Test.Hello", "Hello", new { user = new { first_name = "Sample" } }, new Dictionary<string, MailTemplateContent>
        {
            ["en"] = new("Hi {{ user.first_name }}", "<p>Hello {{ user.first_name }}</p>"),
            ["de"] = new("Hallo {{ user.first_name }}", "<p>Hallo {{ user.first_name }}</p>"),
        }));
}

public sealed record QueueTestMail(string To, bool Fail = false) : ICommand<Result>;

internal sealed class QueueTestMailHandler(IMailSender sender) : IHandler<QueueTestMail, Result>
{
    public async Task<Result> HandleAsync(QueueTestMail command, CancellationToken cancellationToken)
    {
        await sender.QueueAsync(command.To, "Test.Hello", new { user = new { first_name = "Ada" } }, "en", cancellationToken);
        return command.Fail ? Error.Conflict("test.fail", "Rolled back on purpose.") : Result.Success();
    }
}

internal sealed class HookedRecipientDirectory(IMailRecipientDirectory inner, Func<Func<Task>?> hook) : IMailRecipientDirectory
{
    public async Task<Result<RecipientPage>> PageAsync(Coworkee.Contracts.Mailing.RecipientSourceDto source, Guid? after, int size, CancellationToken cancellationToken)
    {
        var page = await inner.PageAsync(source, after, size, cancellationToken);
        if (hook() is { } action)
        {
            await action();
        }

        return page;
    }

    public Task<Result<Coworkee.Contracts.Mailing.RecipientPreviewDto>> PreviewAsync(Coworkee.Contracts.Mailing.RecipientSourceDto source, int first, CancellationToken cancellationToken) =>
        inner.PreviewAsync(source, first, cancellationToken);
}