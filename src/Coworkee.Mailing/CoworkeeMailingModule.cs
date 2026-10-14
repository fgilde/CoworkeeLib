using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.AspNetCore.Http;
using Coworkee.AspNetCore;
using Coworkee.BackgroundJobs;
using Coworkee.Contracts.Mailing;
using Coworkee.Contracts.Configuration;
using Coworkee.Contracts.Settings;
using Coworkee.Contracts;
using Coworkee.Core.Modularity;
using Coworkee.Infrastructure.Persistence;
using Coworkee.OData;
using Coworkee.Settings;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Mailing;

[DependsOn(typeof(CoworkeeSettingsModule), typeof(CoworkeeBackgroundJobsModule))]
public sealed class CoworkeeMailingModule : CoworkeeModule, IWebModule
{
    public override void ConfigureServices(ModuleServiceContext context)
    {
        var services = context.Services;
        services.AddMessagingFromAssembly(typeof(CoworkeeMailingModule).Assembly);
        services.AddSingleton<IModelContributor, MailModelContributor>();
        services.AddSingleton<Coworkee.Infrastructure.Versioning.IVersionedTypeContributor, MailVersionedTypes>();
        services.AddSingleton<IPermissionDefinitionContributor, MailPermissionDefinitions>();
        services.AddODataEntity<OutgoingMail>("Mails", Contracts.Mailing.MailPermissions.Log.View, m => m.HtmlBody);
        services.AddScoped<IODataEntityFilter<OutgoingMail>, OData.OutgoingMailODataFilter>();
        services.AddSingleton<ISettingDefinitionContributor, MailSettingDefinitions>();
        services.AddSingleton<IMailTemplateContributor, CoreMailTemplates>();
        services.AddSingleton<IMailTemplateDefinitionManager, MailTemplateDefinitionManager>();
        services.AddScoped<IMailTemplateCatalog, MailTemplateCatalog>();
        services.AddScoped<IMailTemplateRenderer, MailTemplateRenderer>();
        services.AddScoped<IMailSender, MailSender>();
        services.AddScoped<Application.Privacy.IPersonalDataContributor, MailPersonalData>();
        services.AddScoped<Coworkee.Application.Setup.ISetupCheck, MailSetupCheck>();
        services.AddHttpClient();
        services.AddScoped<MailConnectionResolver>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IMailTransport, SmtpMailTransport>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IMailTransport, SendGridMailTransport>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IMailTransport, MailgunMailTransport>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IMailTransport, MailchimpMailTransport>());
        services.TryAddSingleton(TimeProvider.System);
        services.AddHttpContextAccessor();
        services.AddOptions<NotificationOptions>().BindConfiguration(NotificationOptions.Section);
        services.AddOptions<KeycloakThemeOptions>().BindConfiguration(KeycloakThemeOptions.Section);
        services.AddScoped<IMailUnsubscribeLinks, MailUnsubscribeLinks>();
        services.AddScoped<MailUnsubscribe>();
        services.AddScoped<CampaignMailModels>();
        services.AddScoped<MailCampaignJob>();
    }

    public void ConfigureApplication(WebApplication app)
    {
        var api = app.MapCoworkeeApi("/api/v1/mail").WithTags("Mail").RequireAuthorization();
        api.MapGet("/templates", (IDispatcher d, CancellationToken ct) => d.SendAsync(new GetMailTemplates(), ct).ToHttpResult());
        api.MapPost("/templates", (CreateMailTemplateRequest body, IDispatcher d, CancellationToken ct) =>
            d.SendAsync(new CreateMailTemplate(body), ct).ToHttpResult());
        api.MapDelete("/templates/{name}", (string name, IDispatcher d, CancellationToken ct) =>
            d.SendAsync(new DeleteMailTemplate(name), ct).ToHttpResult());
        api.MapGet("/templates/{name}/{culture}", (string name, string culture, IDispatcher d, CancellationToken ct) =>
            d.SendAsync(new GetMailTemplate(name, culture), ct).ToHttpResult());
        api.MapPut("/templates/{name}/{culture}", (string name, string culture, SaveMailTemplateRequest body, IDispatcher d, CancellationToken ct) =>
            d.SendAsync(new SaveMailTemplate(name, culture, body), ct).ToHttpResult());
        api.MapDelete("/templates/{name}/{culture}", (string name, string culture, IDispatcher d, CancellationToken ct) =>
            d.SendAsync(new ResetMailTemplate(name, culture), ct).ToHttpResult());
        api.MapPost("/templates/{name}/{culture}/preview", (string name, string culture, SaveMailTemplateRequest body, IDispatcher d, CancellationToken ct) =>
            d.SendAsync(new PreviewMailTemplate(name, culture, body), ct).ToHttpResult());
        api.MapPost("/templates/{name}/{culture}/test", (string name, string culture, IDispatcher d, CancellationToken ct) =>
            d.SendAsync(new SendTestMail(name, culture), ct).ToHttpResult());
        api.MapPost("/connection/test", (MailConnectionTestRequest body, IDispatcher d, CancellationToken ct) =>
            d.SendAsync(new TestMailConnection(body), ct).ToHttpResult());
        api.MapGet("/outgoing", ([AsParameters] PageRequest page, OutgoingMailStatus? status, Guid? campaignId, IDispatcher d, CancellationToken ct) =>
            d.SendAsync(new GetOutgoingMails(page, status, campaignId), ct).ToHttpResult());
        api.MapPost("/recipients/preview", (RecipientSourceDto body, IDispatcher d, CancellationToken ct) =>
            d.SendAsync(new PreviewRecipients(body), ct).ToHttpResult());
        api.MapGet("/recipient-lists", (IDispatcher d, CancellationToken ct) => d.SendAsync(new GetRecipientLists(), ct).ToHttpResult());
        api.MapPost("/recipient-lists", (SaveRecipientListRequest body, IDispatcher d, CancellationToken ct) =>
            d.SendAsync(new CreateRecipientList(body), ct).ToHttpResult());
        api.MapPut("/recipient-lists/{id:guid}", (Guid id, SaveRecipientListRequest body, IDispatcher d, CancellationToken ct) =>
            d.SendAsync(new UpdateRecipientList(id, body), ct).ToHttpResult());
        api.MapDelete("/recipient-lists/{id:guid}", (Guid id, IDispatcher d, CancellationToken ct) =>
            d.SendAsync(new DeleteRecipientList(id), ct).ToHttpResult());
        api.MapGet("/campaigns", (IDispatcher d, CancellationToken ct) => d.SendAsync(new GetMailCampaigns(), ct).ToHttpResult());
        api.MapGet("/campaigns/{id:guid}", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new GetMailCampaign(id), ct).ToHttpResult());
        api.MapPost("/campaigns", (SaveMailCampaignRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new CreateMailCampaign(body), ct).ToHttpResult());
        api.MapPut("/campaigns/{id:guid}", (Guid id, SaveMailCampaignRequest body, IDispatcher d, CancellationToken ct) =>
            d.SendAsync(new UpdateMailCampaign(id, body), ct).ToHttpResult());
        api.MapDelete("/campaigns/{id:guid}", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new DeleteMailCampaign(id), ct).ToHttpResult());
        api.MapPost("/campaigns/{id:guid}/test", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new SendMailCampaignTest(id), ct).ToHttpResult());
        api.MapPost("/campaigns/{id:guid}/schedule", (Guid id, ScheduleMailCampaignRequest body, IDispatcher d, CancellationToken ct) =>
            d.SendAsync(new ScheduleMailCampaign(id, body), ct).ToHttpResult());
        api.MapPost("/campaigns/{id:guid}/cancel", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new CancelMailCampaign(id), ct).ToHttpResult());
        api.MapGet("/unsubscribe", (string? token, MailUnsubscribe unsubscribe, CancellationToken ct) => unsubscribe.RunAsync(token, ct)).AllowAnonymous().ExcludeFromDescription();
    }
}

internal sealed class MailPermissionDefinitions : IPermissionDefinitionContributor
{
    public void Define(PermissionDefinitionContext context) =>
        context.Group(MailPermissions.GroupName, "Mail")
            .Add(MailPermissions.Templates.Manage, "Manage mail templates")
            .Add(MailPermissions.Log.View, "View mail log")
            .Add(MailPermissions.Campaigns.Send, "Send mail campaigns");
}

internal sealed class MailSettingDefinitions : ISettingDefinitionContributor
{
    private static readonly SettingScope[] Scopes = [SettingScope.Global, SettingScope.Tenant];

    private static SettingCondition Provider(params string[] providers) => new(MailSettings.Provider, providers);

    public void Define(SettingDefinitionContext context) =>
        context.Group("Mail", "Mail", page: "/admin/mail-settings")
            .Add(MailSettings.UseOwnServer, "Own mail server", SettingType.Bool, [SettingScope.Tenant], "false",
                description: "On: this organisation sends with the settings below. Off: mails go through the system's server.")
            .Add(MailSettings.Provider, "Provider", SettingType.Choice, Scopes, MailProviders.Smtp, choices: [.. MailProviders.All])
            .Add(MailSettings.Host, "SMTP host", SettingType.String, Scopes, enabledWhen: Provider(MailProviders.Smtp))
            .Add(MailSettings.Port, "SMTP port", SettingType.Int, Scopes, "25", enabledWhen: Provider(MailProviders.Smtp))
            .Add(MailSettings.UseSsl, "Use SSL", SettingType.Bool, Scopes, "false", enabledWhen: Provider(MailProviders.Smtp))
            .Add(MailSettings.UserName, "SMTP user name", SettingType.String, Scopes, enabledWhen: Provider(MailProviders.Smtp))
            .Add(MailSettings.Password, "SMTP password", SettingType.Secret, Scopes, enabledWhen: Provider(MailProviders.Smtp))
            .Add(MailSettings.ApiKey, "API key", SettingType.Secret, Scopes,
                enabledWhen: Provider(MailProviders.SendGrid, MailProviders.Mailgun, MailProviders.Mailchimp))
            .Add(MailSettings.MailgunDomain, "Mailgun domain", SettingType.String, Scopes, enabledWhen: Provider(MailProviders.Mailgun))
            .Add(MailSettings.MailgunRegion, "Mailgun region", SettingType.Choice, Scopes, "us", choices: ["us", "eu"], enabledWhen: Provider(MailProviders.Mailgun))
            .Add(MailSettings.From, "Sender address", SettingType.String, Scopes, "noreply@localhost")
            .Add(MailSettings.Whitelist, "Recipient whitelist", SettingType.Text, Scopes,
                description: "Leave empty to allow all recipients. One address or @domain per line.")
            .Add(MailSettings.Newsletters, "Receive newsletters", SettingType.Bool, [SettingScope.Global, SettingScope.User], "true", visibleToClient: true);
}
