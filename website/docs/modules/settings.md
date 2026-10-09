# Settings and app configuration

Coworkee has two kinds of configuration that administrators change at runtime.

| | Settings | App configuration |
|---|---|---|
| For | single values per installation, tenant or user (SMTP host, theme, registration on/off) | a typed options class of the app (limits, feature switches, integrations) |
| Defined by | `ISettingDefinitionContributor` | a C# class, optionally generated from JSON |
| Stored in | settings table, secrets encrypted | `cw.ConfigurationEntries`, read through `IConfiguration` |
| Read with | `ISettingProvider` | `IOptionsMonitor<T>` |
| Admin page | Settings, tabs System and Organisation | Settings, one tab per class (system tenant only) |

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

## Typed app settings

The app describes its settings as classes. Values come from appsettings and the environment; what administrators change is stored in `cw.ConfigurationEntries` and lies on top for every service of the app. The Settings page renders each class with `MudExObjectEditForm`, the way the classic settings page did, and services read `IOptionsMonitor<T>` and see changes within seconds. Values read once at startup apply after a restart.

Every service that should see the changes adds the database layer to its configuration:

```csharp title="Program.cs of the api, the auth server and workers"
builder.Configuration.AddCoworkeeDatabaseConfiguration("myapp"); // the connection string name of the app database
```

### Built-in app settings

Without further code the page shows **App settings**: `CoworkeeAppSettings`, bound to the section `Coworkee`, with self registration (`Registration`), notification digests (`Notifications`) and background jobs (`Jobs`). Wiring values the app host sets (`Jobs:ConnectionStringName`, `Jobs:RunServer`, `Jobs:DashboardPath`, `Notifications:PublicAppUrl`) are locked; lists the configuration binder could only append to (`Jobs:Queues`, `Jobs:RetryDelaysInSeconds`) are hidden.

### Extend the app settings

Derive from `CoworkeeAppSettings` and register the type on both sides. The locks of the built-in sections stay; add your own:

```csharp title="Contracts"
public sealed class MyAppSettings : CoworkeeAppSettings
{
    public BillingSettings Billing { get; set; } = new();
}

public sealed class BillingSettings
{
    public string Currency { get; set; } = "EUR";
    public string? ApiKey { get; set; }
    public string? WebhookSecret { get; set; }
}
```

```csharp title="Server module"
context.Services.AddCoworkeeSettings<MyAppSettings>(context.Configuration, rules => rules
    .Lock(s => s.Billing.Currency)          // shown, never changed
    .Hide(s => s.Billing.WebhookSecret));   // never leaves the server
```

```csharp title="Blazor client"
builder.Services.AddCoworkeeSettings<MyAppSettings>(meta =>
{
    meta.Property(s => s.Billing.ApiKey).WithGroup("Billing");
    meta.Property(s => s.Billing.Currency).WithGroup("Billing");
});
```

Services read `IOptionsMonitor<MyAppSettings>`; the new properties live under `Coworkee:Billing`.

### Replace the app settings

Any other class replaces the built-in one completely, usually with a section of its own:

```csharp
context.Services.AddCoworkeeSettings<ShopSettings>(context.Configuration, section: "Shop", title: "Shop");   // server
builder.Services.AddCoworkeeSettings<ShopSettings>(section: "Shop", title: "Shop");                         // client
```

### Rules

| | |
|---|---|
| `Lock(s => s.A.B)` | the value shows read only; the server keeps what applies whatever comes in. Locking an object locks all its properties |
| `Hide(s => s.A.B)` | the value is neither sent to the browser nor changed |
| secrets | properties named like password, secret, api key, token or connection string come masked; sending the mask back keeps the stored value |
| meta | the client `meta` groups, labels, orders or renders properties like any `MudExObjectEditForm`; locks and hides from the server apply on top. The form shows each section under its heading in two columns (lists over the full width); `WrapInMudItem(i => i.md = 12)` widens a single field |
| scope | typed settings apply to the whole installation and are edited from the system organisation only; per tenant and per user values are settings (above) |

**Restore defaults** drops the values changed in the app; appsettings and environment apply again.

### Editors for file types, sizes and schedules

Three attributes from `Coworkee.Contracts.Configuration` pick a better editor for a property, in the Settings page and in every other object form of the app:

| Attribute | Property | Editor |
|---|---|---|
| `[ContentTypes]` | `List<string>` or `string[]` of MIME types | chips with readable names and icons; common types and groups (images, PDF, Office documents, videos, audio, archives) to pick, other types like `image/x-icon` typed in. Empty accepts every type |
| `[FileSize]` | `long?` or `long` in bytes | a number in KB, MB or GB; empty means no limit |
| `[Cron]` | `string` with a cron expression | presets (every few minutes, hourly, daily, weekly, monthly) and the expression itself, described in words (UTC); an invalid expression is not taken |

```csharp title="Contracts"
public sealed class ImportSettings
{
    [Cron]
    public string Schedule { get; set; } = "0 2 * * *";

    [ContentTypes]
    public List<string> AcceptedFiles { get; set; } = ["text/csv"];

    [FileSize]
    public long? MaxFileSize { get; set; } = 10 * 1024 * 1024;
}
```

The built-in settings use them for the registration documents (`Registration:Documents`) and the digest time (`Notifications:DigestCron`). A `RenderWith` in the client `meta` still wins over the attribute.

### More sections

Further classes show as further tabs next to the app settings:

```csharp title="Server"
builder.Configuration
    .AddCoworkeeAppConfigurationDefaults(SharemeConfiguration.Defaults(), SharemeConfiguration.Section)
    .AddCoworkeeDatabaseConfiguration("shareme");

context.Services.AddCoworkeeAppConfiguration<SharemeConfiguration>(context.Configuration, SharemeConfiguration.Section, SharemeConfiguration.Title,
    rules => rules.Lock(c => c.Storage));
```

```csharp title="Blazor client"
builder.Services.AddCoworkeeAppConfiguration<SharemeConfiguration>(SharemeConfiguration.Section, SharemeConfiguration.Title);
```

`AddCoworkeeAppConfigurationDefaults` adds a JSON file of the section as the lowest layer. Sharemee generates `SharemeConfiguration` from `appconfig.json` with `Nextended.CodeGen`, so the JSON defaults and the class cannot drift apart:

```json title="CodeGen.config.json"
{ "JsonConfigs": [ { "SourceFile": "Configuration/appconfig.json", "RootClassName": "SharemeConfiguration", "Namespace": "Shareme.Contracts.Configuration" } ] }
```

The old address `/admin/configuration` leads to the Settings page.

## Services

`CoworkeeServicesOptions` (section `Coworkee:Services`) names the services of the app: the api, auth server and web app, the Aspire dashboard, the Hangfire dashboard, Mailpit, pgAdmin and every other resource with a web endpoint. The [app host](../hosting/aspire.md) fills it while developing; deployments list their public addresses in appsettings:

```json
{ "Coworkee": { "Services": {
    "myapp-api": { "Url": "https://api.example.com", "HealthPath": "/health" },
    "grafana": { "Url": "https://grafana.example.com", "Title": "Grafana" } } } }
```

```csharp
public sealed class Links(IOptionsMonitor<CoworkeeServicesOptions> services)
{
    public string? Mailpit => services.CurrentValue.GetValueOrDefault("mail")?.Url;
}
```

**Administration > System > Services** shows them as tiles with icon, address and live state; a click opens the service in a new tab. The browser asks the api (`GET /api/v1/services`, settings permission, system organisation), which probes every service every ten seconds: with `HealthPath` a success status counts as running, without it any answer below 500.
