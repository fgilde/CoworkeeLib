using Coworkee.Application;
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.AspNetCore;
using Coworkee.AspNetCore.Http;
using Coworkee.Contracts.Settings;
using Coworkee.Core.Modularity;
using Coworkee.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Settings;

[DependsOn(typeof(CoworkeeApplicationModule))]
public sealed class CoworkeeSettingsModule : CoworkeeModule, IWebModule
{
    public override void ConfigureServices(ModuleServiceContext context)
    {
        var services = context.Services;
        services.AddMessagingFromAssembly(typeof(CoworkeeSettingsModule).Assembly);
        services.AddSingleton<IModelContributor, SettingsModelContributor>();
        services.AddSingleton<IModelContributor, AppConfigurationModelContributor>();
        services.AddSingleton<IPermissionDefinitionContributor, SettingsPermissionDefinitions>();
        services.AddSingleton<ISettingDefinitionManager, SettingDefinitionManager>();
        services.AddScoped<ISettingProvider, SettingProvider>();
        services.AddSingleton<SettingProtector>();
        services.AddScoped<SettingWriter>();
        services.AddScoped<Application.Privacy.IPersonalDataContributor, SettingPersonalData>();
        services.AddScoped<Coworkee.Application.Setup.ISetupStep, SettingsSetupStep>();
        services.AddScoped<IInterceptor, SettingCacheInterceptor>();
        services.AddHybridCache();
    }

    public void ConfigureApplication(WebApplication app)
    {
        var api = app.MapCoworkeeApi("/api/v1/settings").WithTags("Settings").RequireAuthorization();
        api.MapGet("/definitions", (IDispatcher d, CancellationToken ct) => d.SendAsync(new GetSettingDefinitions(), ct).ToHttpResult());
        api.MapGet("/definitions/user", (IDispatcher d, CancellationToken ct) => d.SendAsync(new GetUserSettingDefinitions(), ct).ToHttpResult());
        api.MapGet("/client", (IDispatcher d, CancellationToken ct) => d.SendAsync(new GetClientSettings(), ct).ToHttpResult());
        api.MapGet("/user", (IDispatcher d, CancellationToken ct) => d.SendAsync(new GetUserSettings(), ct).ToHttpResult());
        api.MapPut("/user", (SetSettingsRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new SetUserSettings(body.Values), ct).ToHttpResult());
        var configuration = app.MapCoworkeeApi("/api/v1/configuration").WithTags("Configuration").RequireAuthorization();
        configuration.MapGet("/", (IDispatcher d, CancellationToken ct) => d.SendAsync(new GetAppConfigurations(), ct).ToHttpResult());
        configuration.MapGet("/{section}", (string section, IDispatcher d, CancellationToken ct) => d.SendAsync(new GetAppConfiguration(section), ct).ToHttpResult());
        configuration.MapPut("/{section}", (string section, System.Text.Json.JsonElement body, IDispatcher d, CancellationToken ct) => d.SendAsync(new SaveAppConfiguration(section, body), ct).ToHttpResult());
        configuration.MapDelete("/{section}", (string section, IDispatcher d, CancellationToken ct) => d.SendAsync(new ResetAppConfiguration(section), ct).ToHttpResult());
        foreach (var scope in new[] { SettingScope.Global, SettingScope.Tenant })
        {
            var path = "/" + scope.ToString().ToLowerInvariant();
            api.MapGet(path, (IDispatcher d, CancellationToken ct) => d.SendAsync(new GetManagedSettings(scope), ct).ToHttpResult());
            api.MapPut(path, (SetSettingsRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new SetManagedSettings(scope, body.Values), ct).ToHttpResult());
        }
    }
}

internal sealed class SettingsPermissionDefinitions : IPermissionDefinitionContributor
{
    public void Define(PermissionDefinitionContext context) =>
        context.Group(SettingsPermissions.GroupName, "Settings").Add(SettingsPermissions.Manage, "Manage settings");
}

internal sealed class SettingCacheInterceptor(HybridCache cache) : SaveChangesInterceptor
{
    private bool _pending;

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Detect(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Detect(eventData.Context);
        return result;
    }

    public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        if (_pending)
        {
            _pending = false;
            await cache.RemoveByTagAsync(SettingProvider.CacheTag, cancellationToken);
        }

        return result;
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        if (_pending)
        {
            _pending = false;
            cache.RemoveByTagAsync(SettingProvider.CacheTag).AsTask().GetAwaiter().GetResult();
        }

        return result;
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData) => _pending = false;

    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        _pending = false;
        return Task.CompletedTask;
    }

    private void Detect(DbContext? context) =>
        _pending |= context?.ChangeTracker.Entries<SettingValue>().Any(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted) == true;
}
