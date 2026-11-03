using Coworkee.Application.Messaging;
using Coworkee.AspNetCore;
using Coworkee.Contracts.Configuration;
using Coworkee.Core.Modularity;
using Coworkee.Identity;
using Coworkee.Identity.Domain;
using Coworkee.Mailing;
using Coworkee.Settings;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Account;

[DependsOn(typeof(CoworkeeIdentityModule), typeof(CoworkeeMailingModule), typeof(Coworkee.OData.CoworkeeODataModule))]
public sealed class CoworkeeAccountModule : CoworkeeModule, IWebModule
{
    public override void ConfigureServices(ModuleServiceContext context)
    {
        context.Services.Configure<AccountOptions>(context.Configuration.GetSection(AccountOptions.Section));
        context.Services.Configure<IdentityOptions>(identity => context.Configuration.GetSection(RegistrationOptions.PasswordSection).Bind(identity.Password));
        context.Services.AddMessagingFromAssembly(typeof(CoworkeeAccountModule).Assembly);
        context.Services.AddScoped<IAccountMailer, AccountMailer>();
        context.Services.AddScoped<IMailRecipientDirectory, MailRecipientDirectory>();
        context.Services.AddScoped<Coworkee.Identity.Users.IUserActivationListener, RegistrationApprovedMail>();
        context.Services.AddSingleton<ISettingDefinitionContributor, AccountSettingDefinitions>();
        context.Services.AddSingleton<ISettingDefinitionContributor, SecuritySettingDefinitions>();
        context.Services.AddSingleton<ISettingDefinitionContributor, ActiveRoleSettingDefinitions>();
        context.Services.AddSingleton<ISettingDefinitionContributor, LoginModeSettingDefinitions>();
        context.Services.AddScoped<Coworkee.Identity.Roles.IActiveRoleSwitch, ActiveRoleSetting>();
        context.Services.AddSingleton<Coworkee.Domain.IRealtimeTopicMapper, ActiveRoleSettingTopics>();
        context.Services.AddScoped<PasswordPolicy>();
        context.Services.AddScoped<Application.Setup.ISetupPasswordRules, SettingsSetupPasswordRules>();
        context.Services.Remove(context.Services.Single(d => d.ServiceType == typeof(IPasswordValidator<User>) && d.ImplementationType == typeof(PasswordValidator<User>)));
        context.Services.AddScoped<IPasswordValidator<User>, SettingsPasswordValidator>();
        context.Services.AddScoped<IPasswordValidator<User>, PasswordHistoryValidator>();
    }

    public void ConfigureApplication(WebApplication app) => AccountEndpoints.Map(app);
}
