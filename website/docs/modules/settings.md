# Settings and app configuration

Coworkee has two kinds of configuration that administrators change at runtime.

| | Settings | App configuration |
|---|---|---|
| For | single values per installation, tenant or user (SMTP host, theme, registration on/off) | a typed options class of the app (limits, feature switches, integrations) |
| Defined by | `ISettingDefinitionContributor` | a C# class, optionally generated from JSON |
| Stored in | settings table, secrets encrypted | `cw.ConfigurationEntries`, read through `IConfiguration` |
| Read with | `ISettingProvider` | `IOptionsMonitor<T>` |
| Admin page | Settings | Configuration (system tenant only) |

## Settings

```csharp
internal sealed class MailSettingDefinitions : ISettingDefinitionContributor
{
    public void Define(SettingDefinitionContext context) =>
        context.Group("Mail", "Mail")
            .Add("Mail.Smtp.Host", "SMTP host", SettingType.String, [SettingScope.Global])
            .Add("Mail.Smtp.Port", "SMTP port", SettingType.Int, [SettingScope.Global], defaultValue: "25")
            .Add("Mail.Smtp.Password", "SMTP password", SettingType.Secret, [SettingScope.Global]);
}
```

Types are `String`, `Text`, `Bool`, `Int`, `Secret` and `Choice`; scopes are `Global`, `Tenant` and `User`. The most specific value wins. Secrets are encrypted with ASP.NET Core data protection and never sent to the client; settings with `visibleToClient: true` are.

```csharp
var allowed = await settings.GetAsync<bool>(AccountSettings.AllowRegistration, ct);
```

Defaults can also come from configuration: `Coworkee:Settings:Defaults:Mail.Smtp.Host`. The app host uses that for Mailpit.

![Settings page](../assets/screenshots/settings.png){ .shot }

## Typed app configuration

The app describes its configuration as a class. The database provider overlays `appsettings.json`, so the file holds the defaults and administrators change values without a deployment.

```csharp title="Server"
builder.Configuration
    .AddCoworkeeAppConfigurationDefaults(SharemeConfiguration.Defaults(), SharemeConfiguration.Section)
    .AddCoworkeeDatabaseConfiguration("shareme");

context.Services.AddCoworkeeAppConfiguration<SharemeConfiguration>(context.Configuration, SharemeConfiguration.Section, SharemeConfiguration.Title);
```

```csharp title="Blazor client"
builder.Services.AddCoworkeeAppConfiguration<SharemeConfiguration>(SharemeConfiguration.Section, SharemeConfiguration.Title);
```

The Configuration page renders the class with `MudExObjectEditForm`. Services read `IOptionsMonitor<SharemeConfiguration>` and see changes within seconds.


Sharemee generates `SharemeConfiguration` from `appconfig.json` with `Nextended.CodeGen`, so the JSON defaults and the class cannot drift apart:

```json title="CodeGen.config.json"
{ "JsonConfigs": [ { "SourceFile": "Configuration/appconfig.json", "RootClassName": "SharemeConfiguration", "Namespace": "Shareme.Contracts.Configuration" } ] }
```
