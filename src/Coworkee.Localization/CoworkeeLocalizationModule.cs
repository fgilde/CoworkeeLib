using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Application.Setup;
using Coworkee.AspNetCore;
using Coworkee.Core.Modularity;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Localization.Endpoints;
using Coworkee.Localization.Permissions;
using Coworkee.Localization.Persistence;
using Coworkee.Localization.Resources;
using Coworkee.Localization.Settings;
using Coworkee.Localization.Texts;
using Coworkee.OData;
using Coworkee.Settings;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Localization;

/// <summary>Languages, module texts in embedded JSON resources and the edits of the administrators.</summary>
[DependsOn(typeof(CoworkeeSettingsModule), typeof(CoworkeeODataModule), typeof(Coworkee.Realtime.CoworkeeRealtimeModule))]
public sealed class CoworkeeLocalizationModule : CoworkeeModule, IWebModule
{
    public override void ConfigureServices(ModuleServiceContext context)
    {
        var services = context.Services;
        services.AddMessagingFromAssembly(typeof(CoworkeeLocalizationModule).Assembly);
        services.AddSingleton<IModelContributor, LocalizationModelContributor>();
        services.AddSingleton<IPermissionDefinitionContributor, LocalizationPermissionDefinitions>();
        services.AddSingleton<ISettingDefinitionContributor, LocalizationSettingDefinitions>();
        services.AddSingleton<ILocalizationResourceContributor, CoworkeeTexts>();
        services.AddSingleton<LocalizationResources>();
        services.AddSingleton<Application.Localization.ITextTranslator, ResourceTextTranslator>();
        services.AddScoped<TextStore>();
        services.AddScoped<LocalizationChanges>();
        services.AddScoped<MachineTranslation.TextTranslator>();
        services.AddScoped<MachineTranslation.MissingTexts>();
        services.AddHttpClient(MachineTranslation.TextTranslator.HttpClientName, client =>
        {
            client.BaseAddress = new Uri(context.Configuration["Coworkee:Localization:TranslatorEndpoint"] ?? "https://api.cognitive.microsofttranslator.com/");
            client.Timeout = TimeSpan.FromMinutes(2);
        });
        services.AddODataEntity<Domain.Language>("Languages", Contracts.Localization.LocalizationPermissions.Manage);
        services.Configure<SetupGateOptions>(options => options.AllowedPrefixes.Add("/api/v1/localization/"));
    }

    public void ConfigureApplication(WebApplication app) => app.MapLocalizationEndpoints();
}
