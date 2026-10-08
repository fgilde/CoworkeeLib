# Localization

`Coworkee.Localization` keeps languages and translations in the database and ships the texts of every module as embedded JSON.

```csharp
[DependsOn(typeof(CoworkeeLocalizationModule))]
public sealed class MyAppDatabaseModule : CoworkeeModule
{
    public override void ConfigureServices(ModuleServiceContext context) =>
        context.Services.AddSingleton<ILocalizationResourceContributor, MyAppTexts>();
}

internal sealed class MyAppTexts : ILocalizationResourceContributor
{
    public void Define(LocalizationResourceContext context) => context.AddEmbeddedJson(typeof(MyAppTexts).Assembly);
}
```

Keys are the English texts. A module embeds `Localization/{culture}.json`:

```json title="Localization/de.json"
{
  "Brands": "Marken",
  "Delete {0}? This cannot be undone.": "{0} löschen? Das lässt sich nicht rückgängig machen."
}
```

```xml
<EmbeddedResource Include="Localization\*.json" />
```

## In the client

`CoworkeeLocalizer` translates in pages and components; missing keys fall back to the key and are reported to the server, where they show up under *Translations*.

```razor
@inject CoworkeeLocalizer L
<MudButton>@L["Save"]</MudButton>
<MudText>@L["Delete {0}? This cannot be undone.", brand.Name]</MudText>
```

The user picks the language in the app bar. MudBlazor's own texts (pager, filters, dialogs) are translated as well. Set the culture before the first render:

```csharp title="Program.cs"
var host = builder.Build();
await host.Services.InitializeCoworkeeClientAsync();
await host.RunAsync();
```

## Administration

- *Languages* (`/admin/languages`): every culture, grouped by language, with a switch. Switching one on translates the texts it lacks with the Azure AI Translator when `Localization.TranslatorKey` (and `Localization.TranslatorRegion`) are set in the settings; a button translates what is still missing later. The star marks the default language, which cannot be switched off.

![Languages](../assets/screenshots/languages.png){ .shot }
- *Translations* (`/admin/translations`): every key with the module text and the edited text per language. Edits win over the module texts.

Both need `Localization.Manage`.

Every change reaches all clients at once (`global:localization` realtime topic): they reload their texts, and users whose language was switched off move to the default language with a short notice.
