using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.AspNetCore;
using Coworkee.AspNetCore.Http;
using Coworkee.BackgroundJobs;
using Coworkee.Contracts;
using Coworkee.Contracts.Mailing;
using Coworkee.Contracts.Settings;
using Coworkee.Core.Modularity;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Settings;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Coworkee.Mailing;

public static class MailSettings
{
    public const string Host = "Mail.Smtp.Host";
    public const string Port = "Mail.Smtp.Port";
    public const string UseSsl = "Mail.Smtp.UseSsl";
    public const string UserName = "Mail.Smtp.UserName";
    public const string Password = "Mail.Smtp.Password";
    public const string From = "Mail.From";
    public const string Whitelist = "Mail.Whitelist";
}

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
        services.AddSingleton<ISettingDefinitionContributor, MailSettingDefinitions>();
        services.AddSingleton<IMailTemplateContributor, CoreMailTemplates>();
        services.AddSingleton<IMailTemplateDefinitionManager, MailTemplateDefinitionManager>();
        services.AddScoped<IMailTemplateRenderer, MailTemplateRenderer>();
        services.AddScoped<IMailSender, MailSender>();
        services.AddScoped<Coworkee.Application.Setup.ISetupCheck, MailSetupCheck>();
        services.TryAddSingleton<ISmtpTransport, MailKitSmtpTransport>();
        services.TryAddSingleton(TimeProvider.System);
    }

    public void ConfigureApplication(WebApplication app)
    {
        var api = app.MapGroup("/api/v1/mail").WithTags("Mail").RequireAuthorization();
        api.MapGet("/templates", (IDispatcher d, CancellationToken ct) => d.SendAsync(new GetMailTemplates(), ct).ToHttpResult());
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
        api.MapGet("/outgoing", ([AsParameters] PageRequest page, OutgoingMailStatus? status, IDispatcher d, CancellationToken ct) =>
            d.SendAsync(new GetOutgoingMails(page, status), ct).ToHttpResult());
    }
}

internal sealed class MailPermissionDefinitions : IPermissionDefinitionContributor
{
    public void Define(PermissionDefinitionContext context) =>
        context.Group(MailPermissions.GroupName, "Mail")
            .Add(MailPermissions.Templates.Manage, "Manage mail templates")
            .Add(MailPermissions.Log.View, "View mail log");
}

internal sealed class MailSettingDefinitions : ISettingDefinitionContributor
{
    private static readonly SettingScope[] Scopes = [SettingScope.Global, SettingScope.Tenant];

    public void Define(SettingDefinitionContext context) =>
        context.Group("Mail", "Mail")
            .Add(MailSettings.Host, "SMTP host", SettingType.String, Scopes)
            .Add(MailSettings.Port, "SMTP port", SettingType.Int, Scopes, "25")
            .Add(MailSettings.UseSsl, "Use SSL", SettingType.Bool, Scopes, "false")
            .Add(MailSettings.UserName, "SMTP user name", SettingType.String, Scopes)
            .Add(MailSettings.Password, "SMTP password", SettingType.Secret, Scopes)
            .Add(MailSettings.From, "Sender address", SettingType.String, Scopes, "noreply@localhost")
            .Add(MailSettings.Whitelist, "Recipient whitelist", SettingType.Text, Scopes,
                description: "Leave empty to allow all recipients. One address or @domain per line.");
}
