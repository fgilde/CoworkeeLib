#if (auditing)
using Coworkee.Auditing;
#endif
using Coworkee.AuthServer;
using Coworkee.Core.Modularity;
using Coworkee.Localization;
using Coworkee.Localization.Resources;
using Coworkee.Mailing;
#if (notifications)
using Coworkee.Notifications;
#endif
using Coworkee.Theming;
using Microsoft.Extensions.DependencyInjection;

namespace MyApp.Infrastructure;

[DependsOn(typeof(MyAppInfrastructureModule), typeof(CoworkeeAuthStoreModule), typeof(CoworkeeMailingModule)
#if (auditing)
    , typeof(CoworkeeAuditingModule)
#endif
    , typeof(CoworkeeThemingModule)
#if (notifications)
    , typeof(CoworkeeNotificationsModule)
#endif
    , typeof(Coworkee.Account.CoworkeeAccountModule), typeof(CoworkeeLocalizationModule)
#if (withBackup)
    , typeof(Coworkee.Backup.CoworkeeBackupModule)
#endif
#if (chat)
    , typeof(Coworkee.Chat.CoworkeeChatModule)
#endif
#if (reports)
    , typeof(Coworkee.Reporting.CoworkeeReportingModule)
#endif
#if (ai)
    , typeof(Coworkee.Ai.CoworkeeAiModule)
#endif
#if (files)
    , typeof(Coworkee.Files.CoworkeeFilesModule)
#endif
#if (features)
    , typeof(Coworkee.Features.CoworkeeFeaturesModule)
#endif
#if (social)
    , typeof(Coworkee.Social.CoworkeeSocialModule)
#endif
#if (workflows)
    , typeof(Coworkee.Workflows.CoworkeeWorkflowsModule)
#endif
#if (webhooks)
    , typeof(Coworkee.Webhooks.CoworkeeWebhooksModule)
#endif
#if (extendedAttributes)
    , typeof(Coworkee.ExtendedAttributes.CoworkeeExtendedAttributesModule)
#endif
#if (elasticsearch)
    , typeof(Coworkee.Search.Elasticsearch.CoworkeeElasticsearchModule)
#endif
#if (eventBus == "inProcess")
    , typeof(Coworkee.EventBus.CoworkeeEventBusModule)
#elseif (eventBus == "rabbitMq")
    , typeof(Coworkee.EventBus.RabbitMq.CoworkeeRabbitMqEventBusModule)
#endif
#if (samples)
    , typeof(MyApp.Catalog.MyAppCatalogModule), typeof(MyApp.Documents.MyAppDocumentsModule)
#endif
    )]
public sealed class MyAppDatabaseModule : CoworkeeModule
{
    public override void ConfigureServices(ModuleServiceContext context) =>
        context.Services.AddSingleton<ILocalizationResourceContributor, MyAppTexts>();
}
