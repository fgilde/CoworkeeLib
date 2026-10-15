using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.AspNetCore;
using Coworkee.AspNetCore.Http;
using Coworkee.Contracts.Configuration;
using Coworkee.Core.Modularity;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Workflows.Elsa.Activities;
using Elsa.Extensions;
using Elsa.Persistence.EFCore;
using Elsa.Persistence.EFCore.Extensions;
using Elsa.Persistence.EFCore.Modules.Management;
using Elsa.Persistence.EFCore.Modules.Runtime;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Workflows.Elsa;

/// <summary>
/// Elsa Workflows 3 inside the app: its tables in the app's database, its API under /api/elsa for users with
/// <see cref="ElsaPermissions.Manage"/>, Coworkee activities and triggers from workflow transitions, decisions and watched entities.
/// </summary>
[DependsOn(typeof(CoworkeeWorkflowsModule))]
public sealed class CoworkeeWorkflowsElsaModule : CoworkeeModule, IWebModule
{
    public const string RoutePrefix = "api/elsa";

    public override void ConfigureServices(ModuleServiceContext context)
    {
        var services = context.Services;
        var provider = DatabaseProviders.Normalize(context.Configuration[DatabaseOptions.ProviderKey]);
        services.AddCoworkeeElsa(_ => { });
        services.AddMessagingFromAssembly(typeof(CoworkeeWorkflowsElsaModule).Assembly);
        services.AddSingleton<IPermissionDefinitionContributor, ElsaPermissionDefinitions>();
        services.AddScoped<IInterceptor, EntityChangeInterceptor>();
        services.AddHttpClient(QueryOData.HttpClientName, (sp, http) =>
        {
            var section = sp.GetRequiredService<IConfiguration>().GetSection(ElsaWorkflowOptions.Section);
            http.BaseAddress = new Uri(section["ApiUrl"] is { Length: > 0 } url ? url.TrimEnd('/') + "/" : "http://localhost/");
            if (section["ApiKey"] is { Length: > 0 } key)
            {
                http.DefaultRequestHeaders.Authorization = new("Bearer", key);
            }
        });

        // the gate in ConfigureApplication lets only users with the permission reach the API, which then trusts them fully
        global::Elsa.EndpointSecurityOptions.DisableSecurity();
        services.AddElsa(elsa =>
        {
            elsa.UseWorkflowManagement(management => management.UseEntityFrameworkCore(ef => UseDatabase(ef, provider)));
            elsa.UseWorkflowRuntime(runtime => runtime.UseEntityFrameworkCore(ef => UseDatabase(ef, provider)));
            elsa.UseWorkflowsApi();
            elsa.AddActivitiesFrom<CoworkeeWorkflowsElsaModule>();
        });
    }

    public void ConfigureApplication(WebApplication app)
    {
        app.UseWhen(context => context.Request.Path.StartsWithSegments("/" + RoutePrefix), gate => gate.Use(ElsaGate.InvokeAsync));
        app.UseWorkflowsApi(RoutePrefix);
        app.MapCoworkeeApi("/api/v1/workflows/elsa").WithTags("Workflows").RequireAuthorization()
            .MapGet("/studio", async (IConfiguration configuration, IPermissionChecker permissions, CancellationToken ct) =>
                !await permissions.IsGrantedAsync(ElsaPermissions.Manage, ct) ? Results.Forbid()
                : configuration[$"{ElsaWorkflowOptions.Section}:StudioUrl"] is { Length: > 0 } url ? Results.Redirect(url)
                : Results.NotFound());
    }

    // same database as the app; Elsa keeps its own tables and migrations
    private static void UseDatabase<TFeature, TDbContext>(PersistenceFeatureBase<TFeature, TDbContext> feature, string provider)
        where TDbContext : ElsaDbContextBase
        where TFeature : PersistenceFeatureBase<TFeature, TDbContext>
    {
        Func<IServiceProvider, string> connectionString = ConnectionString;
        _ = provider switch
        {
            DatabaseProviders.SqlServer => feature.UseSqlServer(connectionString),
            DatabaseProviders.Sqlite => feature.UseSqlite(connectionString),
            _ => feature.UsePostgreSql(connectionString),
        };
    }

    private static string ConnectionString(IServiceProvider services)
    {
        if (services.GetRequiredService<IConfiguration>()[$"{ElsaWorkflowOptions.Section}:ConnectionString"] is { Length: > 0 } own)
        {
            return own;
        }

        using var scope = services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<CoworkeeDbContext>().Database.GetConnectionString()!;
    }
}

public static class ElsaPermissions
{
    public const string GroupName = "Elsa";

    /// <summary>Design, run and inspect Elsa workflows through /api/elsa (and Elsa Studio).</summary>
    public const string Manage = "Workflows.Elsa.Manage";
}

internal sealed class ElsaPermissionDefinitions : IPermissionDefinitionContributor
{
    public void Define(PermissionDefinitionContext context) =>
        context.Group(ElsaPermissions.GroupName, "Elsa workflows")
            .Add(ElsaPermissions.Manage, "Design and run Elsa workflows");
}

/// <summary>Authenticates requests to the Elsa API itself (the app authenticates later in the pipeline) and lets only users with the permission through.</summary>
internal static class ElsaGate
{
    public static async Task InvokeAsync(HttpContext context, Func<Task> next)
    {
        if (await context.AuthenticateAsync() is not { Succeeded: true } result)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        context.User = result.Principal;
        if (!await context.RequestServices.GetRequiredService<IPermissionChecker>().IsGrantedAsync(ElsaPermissions.Manage, context.RequestAborted))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        await next();
    }
}
