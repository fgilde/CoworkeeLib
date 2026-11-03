using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Application.Retention;
using Coworkee.Application.Status;
using Coworkee.AspNetCore;
using Coworkee.AspNetCore.Http;
using Coworkee.BackgroundJobs;
using Coworkee.Contracts;
using Coworkee.Contracts.Configuration;
using Coworkee.Contracts.Mailing;
using Coworkee.Core.Modularity;
using Coworkee.Infrastructure.Persistence;
using Coworkee.OData;
using Coworkee.Settings;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Coworkee.Mailing;

[DependsOn(typeof(CoworkeeSettingsModule), typeof(CoworkeeBackgroundJobsModule))]
public sealed class CoworkeeMailingModule : CoworkeeModule, IWebModule
{
    public override void ConfigureServices(ModuleServiceContext context)
    {
        var services = context.Services;
        services.AddMessagingFromAssembly(typeof(CoworkeeMailingModule).Assembly);
        services.AddSystemStatus<MailStatus>();
        services.AddRetentionTarget<MailLogRetention>();
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
        services.TryAddScoped<Coworkee.OData.Transfer.IExportMailer, ExportMailer>();
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
        services.AddOptions<MailOptions>().BindConfiguration(MailOptions.Section);
        services.AddRecurringJob<StuckMailSweep>(StuckMailSweep.Id, StuckMailSweep.Cron);
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
        api.MapPost("/templates/{name}/{culture}", (string name, string culture, AddMailTemplateLanguageRequest body, IDispatcher d, CancellationToken ct) =>
            d.SendAsync(new AddMailTemplateLanguage(name, culture, body), ct).ToHttpResult());
        api.MapPut("/templates/{name}/{culture}", (string name, string culture, SaveMailTemplateRequest body, IDispatcher d, CancellationToken ct) =>
            d.SendAsync(new SaveMailTemplate(name, culture, body), ct).ToHttpResult());
        api.MapDelete("/templates/{name}/{culture}", (string name, string culture, IDispatcher d, CancellationToken ct) =>
            d.SendAsync(new ResetMailTemplate(name, culture), ct).ToHttpResult());
        api.MapPost("/templates/{name}/{culture}/preview", (string name, string culture, SaveMailTemplateRequest body, IDispatcher d, CancellationToken ct) =>
            d.SendAsync(new PreviewMailTemplate(name, culture, body), ct).ToHttpResult());
        api.MapGet("/templates/{name}/{culture}/history", (string name, string culture, IDispatcher d, CancellationToken ct) =>
            d.SendAsync(new GetMailTemplateHistory(name, culture), ct).ToHttpResult());
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
        api.MapGet("/unsubscribe", (HttpContext http, string? token, MailUnsubscribe unsubscribe, CancellationToken ct) => unsubscribe.ConfirmAsync(http, token, ct))
            .AllowAnonymous().ExcludeFromDescription();
        api.MapPost("/unsubscribe", (HttpContext http, MailUnsubscribe unsubscribe, CancellationToken ct) => unsubscribe.UnsubscribeAsync(http, ct))
            .AllowAnonymous().DisableAntiforgery().ExcludeFromDescription();
    }
}
