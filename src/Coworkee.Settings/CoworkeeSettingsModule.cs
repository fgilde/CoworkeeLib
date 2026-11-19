using Coworkee.Application;
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Application.Status;
using Coworkee.AspNetCore;
using Coworkee.AspNetCore.Http;
using Coworkee.Contracts.Settings;
using Coworkee.Core.Modularity;
using Coworkee.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
        services.AddSingleton<ISettingDefinitionContributor, TableSettingDefinitions>();
        services.AddSingleton<ISettingDefinitionContributor, EditorSettingDefinitions>();
        services.AddSingleton<ISettingDefinitionContributor, HeaderSettingDefinitions>();
        services.AddSingleton<ISettingDefinitionContributor, DialogSettingDefinitions>();
        services.AddSingleton<ISettingDefinitionContributor, FilePickerSettingDefinitions>();
        services.AddSingleton<ISettingDefinitionContributor, CacheSettingDefinitions>();
        services.AddScoped<Application.Caching.ICacheDefaults, SettingCacheDefaults>();
        services.AddScoped<ISettingProvider, SettingProvider>();
        services.AddSingleton<SettingProtector>();
        services.AddScoped<SettingWriter>();
        services.AddScoped<Application.Privacy.IPersonalDataContributor, SettingPersonalData>();
        services.AddScoped<Application.Privacy.IPersonalDataContributor, ClientValuePersonalData>();
        services.Configure<ClientValueOptions>(context.Configuration.GetSection(ClientValueOptions.Section));
        services.AddScoped<Coworkee.Application.Setup.ISetupStep, SettingsSetupStep>();
        services.AddScoped<IInterceptor, SettingCacheInterceptor>();
        services.AddHybridCache();
        services.AddHttpClient(ConfiguredServiceSource.Client).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IServiceSource, ConfiguredServiceSource>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IServiceSource, AspireUIServiceSource>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IServiceSource, HealthCheckServiceSource>());
        services.TryAddScoped<IServiceDirectory, ServiceDirectory>();
        services.AddSystemStatus<VersionsStatus>().AddSystemStatus<DatabaseStatus>();
        // the overrides of the Local copies page (Coworkee.OData reads them live); no Settings tab, the page has its own table
        services.AddSingleton(new AppConfigurationRegistration(Contracts.Data.ClientEntitySetOverride.Section, "Local copies",
            typeof(Dictionary<string, Contracts.Data.ClientEntitySetOverride>)));
        if (!services.HasAppSettings())
        {
            services.AddCoworkeeSettings<CoworkeeAppSettings>(context.Configuration);
        }
    }

    public void ConfigureApplication(WebApplication app)
    {
        var api = app.MapCoworkeeApi("/api/v1/settings").WithTags("Settings").RequireAuthorization();
        api.MapGet("/definitions", (IDispatcher d, CancellationToken ct) => d.SendAsync(new GetSettingDefinitions(), ct).ToHttpResult());
        api.MapGet("/definitions/user", (IDispatcher d, CancellationToken ct) => d.SendAsync(new GetUserSettingDefinitions(), ct).ToHttpResult());
        api.MapGet("/client", (IDispatcher d, CancellationToken ct) => d.SendAsync(new GetClientSettings(), ct).ToHttpResult());
        api.MapGet("/user", (IDispatcher d, CancellationToken ct) => d.SendAsync(new GetUserSettings(), ct).ToHttpResult());
        api.MapPut("/user", (SetSettingsRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new SetUserSettings(body.Values), ct).ToHttpResult());
        api.MapGet("/head-script", (IDispatcher d, CancellationToken ct) => d.SendAsync(new GetHeadScript(), ct).ToHttpResult()).AllowAnonymous();
        api.MapPut("/head-script", (SetHeadScriptRequest body, IDispatcher d, CancellationToken ct) =>
            d.SendAsync(new SetHeadScript(body.Enabled, body.Value), ct).ToHttpResult());
        var values = app.MapCoworkeeApi("/api/v1/client-values").WithTags("Client values").RequireAuthorization();
        values.MapGet("/", (IDispatcher d, CancellationToken ct) => d.SendAsync(new GetClientValues(), ct).ToHttpResult());
        values.MapGet("/keys", (IDispatcher d, CancellationToken ct) => d.SendAsync(new GetClientValueKeys(), ct).ToHttpResult());
        values.MapGet("/value", (string? key, IDispatcher d, CancellationToken ct) => d.SendAsync(new GetClientValue(key ?? string.Empty), ct).ToHttpResult());
        values.MapPut("/", (SetClientValueRequest body, IDispatcher d, CancellationToken ct) =>
            d.SendAsync(new SetClientValue(body.Key, body.Value, body.Scope), ct).ToHttpResult());
        values.MapDelete("/", (string? key, ClientValueScope? scope, IDispatcher d, CancellationToken ct) =>
            d.SendAsync(new DeleteClientValue(key ?? string.Empty, scope ?? ClientValueScope.User), ct).ToHttpResult());
        var configuration = app.MapCoworkeeApi("/api/v1/configuration").WithTags("Configuration").RequireAuthorization();
        configuration.MapGet("/", (IDispatcher d, CancellationToken ct) => d.SendAsync(new GetAppConfigurations(), ct).ToHttpResult());
        configuration.MapGet("/{section}", (string section, IDispatcher d, CancellationToken ct) => d.SendAsync(new GetAppConfiguration(section), ct).ToHttpResult());
        configuration.MapPut("/{section}", (string section, System.Text.Json.JsonElement body, IDispatcher d, CancellationToken ct) => d.SendAsync(new SaveAppConfiguration(section, body), ct).ToHttpResult());
        configuration.MapDelete("/{section}", (string section, IDispatcher d, CancellationToken ct) => d.SendAsync(new ResetAppConfiguration(section), ct).ToHttpResult());
        app.MapCoworkeeApi("/api/v1/services").WithTags("Services").RequireAuthorization()
            .MapGet("/", (IDispatcher d, CancellationToken ct) => d.SendAsync(new GetServices(), ct).ToHttpResult());
        app.MapCoworkeeApi("/api/v1/system").WithTags("System").RequireAuthorization()
            .MapGet("/status", (IDispatcher d, CancellationToken ct) => d.SendAsync(new GetSystemStatus(), ct).ToHttpResult());
        foreach (var scope in new[] { SettingScope.Global, SettingScope.Tenant })
        {
            var path = "/" + scope.ToString().ToLowerInvariant();
            api.MapGet(path, (IDispatcher d, CancellationToken ct) => d.SendAsync(new GetManagedSettings(scope), ct).ToHttpResult());
            api.MapPut(path, (SetSettingsRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new SetManagedSettings(scope, body.Values), ct).ToHttpResult());
        }
    }
}
