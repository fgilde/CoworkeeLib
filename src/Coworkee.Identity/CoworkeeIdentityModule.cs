using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Application.Setup;
using Coworkee.Application;
using Coworkee.AspNetCore;
using Coworkee.Core.Modularity;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Identity.OData;
using Coworkee.Identity.Permissions;
using Coworkee.Identity.Persistence;
using Coworkee.Identity.Setup;
using Coworkee.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Coworkee.Identity;

[DependsOn(typeof(CoworkeeApplicationModule))]
public sealed class CoworkeeIdentityModule : CoworkeeModule, IWebModule
{
    public override void ConfigureServices(ModuleServiceContext context)
    {
        var services = context.Services;
        services.AddMessagingFromAssembly(typeof(CoworkeeIdentityModule).Assembly);
        services.AddSingleton<IModelContributor, IdentityModelContributor>();
        services.AddSingleton<IPermissionDefinitionContributor, IdentityPermissionDefinitions>();
        services.AddHybridCache();
        services.AddScoped<IPermissionChecker, PermissionChecker>();
        services.AddSingleton<PermissionCache>();
        services.AddScoped<IInterceptor, PermissionCacheInterceptor>();
        services.AddScoped<IInterceptor, AccessChangeInterceptor>();
        services.AddScoped<IResourceAccessReader, ResourceAccessReader>();
        services.AddScoped<IUserDirectory, Users.UserDirectory>();
        services.AddScoped<Users.Profile.UserChanges>();
        services.AddScoped<ITenantDirectory, Users.TenantDirectory>();
        services.AddSingleton<Coworkee.Domain.IRealtimeTopicMapper, IdentityRealtimeTopics>();
        services.AddSingleton<SetupToken>();
        services.AddScoped<Coworkee.Application.Setup.ISetupCheck, DatabaseSetupCheck>();
        services.AddSingleton<SystemStateCache>();
        services.AddIdentityODataSets();
        services.AddScoped<SystemInitializer>();
        services.AddScoped<IdentitySeeder>();
        services.AddHostedService<SetupTokenAnnouncer>();
        services.AddOptions<SetupGateOptions>();

        services.AddIdentityCore<User>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 8;
                options.Lockout.MaxFailedAccessAttempts = 10;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddDefaultTokenProviders();
        services.AddScoped<IUserStore<User>>(provider => new Users.CoworkeeUserStore(provider.GetRequiredService<CoworkeeDbContext>()) { AutoSaveChanges = false });
    }

    public void ConfigureApplication(WebApplication app)
    {
        app.Use(async (httpContext, next) =>
        {
            var path = httpContext.Request.Path;
            var allowed = httpContext.RequestServices.GetRequiredService<IOptions<SetupGateOptions>>().Value.AllowedPrefixes;
            if (path.StartsWithSegments("/api")
                && !allowed.Any(prefix => path.StartsWithSegments(prefix))
                && !await httpContext.RequestServices.GetRequiredService<SystemStateCache>().IsInitializedAsync(httpContext.RequestAborted))
            {
                await TypedResults.Problem(
                    title: "The system has not been set up yet.",
                    statusCode: StatusCodes.Status503ServiceUnavailable,
                    extensions: new Dictionary<string, object?>(StringComparer.Ordinal) { ["code"] = "setup_required" })
                    .ExecuteAsync(httpContext);
                return;
            }

            await next(httpContext);
        });

        IdentityEndpoints.Map(app);
    }
}
