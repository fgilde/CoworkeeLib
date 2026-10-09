using System.Net.Http.Json;
using Coworkee.Application.Messaging;
using Coworkee.AspNetCore;
using Coworkee.AspNetCore.Http;
using Coworkee.Contracts.Features;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Modularity;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Identity.Setup;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

[assembly: AssemblyFixture(typeof(Coworkee.Features.Tests.FeaturesApp))]
[assembly: CollectionBehavior(CollectionBehavior.CollectionPerAssembly)]

namespace Coworkee.Features.Tests;

public sealed class FeaturesApp : PostgresFixture
{
    public WebApplication App { get; private set; } = null!;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration["Coworkee:SetupToken"] = "token";
        builder.Configuration["ConnectionStrings:test"] = ConnectionString;
        builder.AddCoworkee<TestFeaturesModule>();
        builder.Services.AddTestAuthentication();
        App = builder.Build();
        await using (var scope = App.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<FeaturesTestDbContext>().Database.EnsureCreatedAsync();
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
        App.Services.GetRequiredService<SystemStateCache>().Reset();
        var response = await App.GetTestClient().PostAsJsonAsync("/api/v1/setup/complete",
            new CompleteSetupRequest("token", "Host", "admin@host.test", "Admin#12345", "Ada", "Admin", null, null));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SetupResultDto>())!;
    }

    public HttpClient As(Guid userId, Guid tenantId) => App.GetTestClient().AsUser(userId, tenantId);

    public async Task<T> InDbAsync<T>(Func<FeaturesTestDbContext, Task<T>> action)
    {
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(null, null));
        await using var scope = App.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<FeaturesTestDbContext>());
    }

    public Task<(Guid UserId, Guid TenantId)> CreateTenantAdminAsync() => InDbAsync(async db =>
    {
        var tenant = new Tenant { Name = "Other", Identifier = "other-" + Guid.NewGuid().ToString("N")[..8] };
        var email = $"admin-{Guid.NewGuid():N}@other.test";
        var user = new User { TenantId = tenant.Id, UserName = email, NormalizedUserName = email.ToUpperInvariant(), Email = email, NormalizedEmail = email.ToUpperInvariant() };
        var adminRole = await db.Set<Role>().Where(r => r.IsSystem && r.Name == SystemRoles.Admin).Select(r => r.Id).SingleAsync();
        db.AddRange(tenant, user, new IdentityUserRole<Guid> { UserId = user.Id, RoleId = adminRole });
        await db.SaveChangesAsync();
        return (user.Id, tenant.Id);
    });
}

public sealed class FeaturesTestDbContext(DbContextOptions<FeaturesTestDbContext> options, ICurrentUser currentUser, IEnumerable<IModelContributor> contributors)
    : CoworkeeDbContext(options, currentUser, contributors);

public static class TestFeatures
{
    public const string Reports = "Test.Reports";
    public const string Export = "Test.Export";
    public const string MaxProjects = "Test.MaxProjects";
}

[RequiresFeature(TestFeatures.Reports)]
public sealed record GetReport : IQuery<Result<string>>;

internal sealed class GetReportHandler : IHandler<GetReport, Result<string>>
{
    public Task<Result<string>> HandleAsync(GetReport query, CancellationToken cancellationToken) => Task.FromResult(Result<string>.Success("report"));
}

internal sealed class TestFeatureDefinitions : IFeatureDefinitionContributor
{
    public void Define(FeatureDefinitionContext context) =>
        context.Group("Test", "Test")
            .Add(TestFeatures.Reports, "Reports")
            .Add(TestFeatures.Export, "Export")
            .Add(TestFeatures.MaxProjects, "Max projects", FeatureType.Int, "3");
}

[DependsOn(typeof(CoworkeeFeaturesModule))]
public sealed class TestFeaturesModule : CoworkeeModule, IWebModule
{
    public override void ConfigureServices(ModuleServiceContext context)
    {
        context.Services.AddCoworkeeDbContext<FeaturesTestDbContext>((provider, options) =>
            options.UseNpgsql(provider.GetRequiredService<IConfiguration>().GetConnectionString("test")));
        context.Services.AddMessagingFromAssembly(typeof(TestFeaturesModule).Assembly);
        context.Services.AddSingleton<IFeatureDefinitionContributor, TestFeatureDefinitions>();
    }

    public void ConfigureApplication(WebApplication app)
    {
        var test = app.MapCoworkeeApi("/api/v1/test").RequireAuthorization();
        test.MapGet("/report", (IDispatcher d, CancellationToken ct) => d.SendAsync(new GetReport(), ct).ToHttpResult());
        test.MapGet("/export", () => TypedResults.Ok("export")).RequireFeature(TestFeatures.Export);
    }
}
