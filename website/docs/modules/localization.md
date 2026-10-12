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

## Initial languages

Without configuration an installation offers English plus every culture a module ships texts for (today `en` and `de`), English as default. `LocalizationOptions` (section `Coworkee:Localization`) narrow that down; setup stores exactly these languages, after that administrators change them on the *Languages* page.

```json title="appsettings.json"
{
  "Coworkee": {
    "Localization": { "Languages": [ "en", "de" ], "DefaultLanguage": "de" }
  }
}
```

Any culture works, not only the shipped ones: languages without shipped texts are [machine translated](#machine-translation). The host does not start with an unknown culture or a default that is not among the languages. `coworkee new --languages en,de,fr --default-language de` writes the same values into a new app.

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

- *Languages* (`/admin/languages`): every culture, grouped by language, with a switch. Switching one on translates the texts it lacks with the [translation service](#machine-translation) of the settings; a button translates what is still missing later. The star marks the default language, which cannot be switched off.

![Languages](../assets/screenshots/languages.png){ .shot }
- *Translations* (`/admin/translations`): every key with the module text and the edited text per language. Edits win over the module texts.

Both need `Localization.Manage`.

Every change reaches all clients at once (`global:localization` realtime topic): they reload their texts, and users whose language was switched off move to the default language with a short notice.

## Machine translation

Texts a language lacks are machine translated: when an administrator switches the language on, with the button on the *Languages* page, and after every start for the switched on languages (a language from `LocalizationOptions` without shipped texts, or the new texts of an update). Translations are stored like edits, so the *Translations* page shows and corrects them; the shipped texts (`en`, `de`) stay the source of truth. Placeholders like `{0}` are kept out of the translation, and a translation that lost one is dropped. A service that fails (a used up quota, a server that is down) ends the run with what it translated so far; the rest follows on the next start or with the button.

| Service | Cost | Account | Where the texts go | Good to know |
|---|---|---|---|---|
| `libreTranslate` | free, open source (AGPL) | none | your own server or container | about 600 MB image plus about 100 MB per language; good quality for the big languages; default of new apps |
| `myMemory` | free | none, optional contact email | public service of Translated | 5,000 characters a day per address, 50,000 with an email; one text of up to 500 bytes per request. The library texts of one language are about 20,000 characters, so a whole language takes days: fine for a few new texts |
| `deepL` | free plan 500,000 characters a month | key | DeepL | keys ending in `:fx` use `api-free.deepl.com`; best quality for European languages |
| `azure` | free tier 2 million characters a month | key and region | Microsoft | over 100 languages |
| `google` | about 500,000 characters a month free | key | Google | Cloud Translation Basic (v2) |
| `ai` | per token of the model | the [AI assistant](ai.md) configured | the model's provider | natural wording; uses the provider, model and key of the AI settings, also with the assistant switched off |
| `none` | | | | missing texts show in English |

Unofficial endpoints, such as the free Google address that browser extensions and some headphone apps call (`translate.googleapis.com` with `client=gtx`), are left out on purpose: they come without terms of use for third parties, block or rate-limit without notice and can change any day.

### Settings

The settings group *Translation* (global only, `Localization.*`):

| Setting | Meaning |
|---|---|
| `Localization.TranslationProvider` | One of the services above; default `azure`, which does nothing until a key is set |
| `Localization.TranslatorKey` | Key of DeepL, Azure or Google, the optional API key of a LibreTranslate server; a secret, masked in the form |
| `Localization.TranslatorRegion` | Region of the Azure translator resource |
| `Localization.TranslationEndpoint` | Address of the LibreTranslate server; for the others an optional replacement of the public address (a proxy) |
| `Localization.TranslationEmail` | Contact for MyMemory, raises the quota to 50,000 characters a day |
| `Localization.TranslationSource` | The language translations start from, default `en` |
| `Localization.AutoTranslate` | Translate when a language is switched on and after the start (default `true`); off, only the button translates |

Texts are keyed by their English wording, so English is the natural source. With `TranslationSource` set to `de`, a text starts from its German wording when there is one (shipped or edited) and from English otherwise; a language never translates from itself, so `de-CH` starts from English.

### In the app host

`UseLibreTranslate` adds a LibreTranslate container that loads only the models of the given languages and the source language (`LT_LOAD_ONLY`, English is always there), keeps them in the volume `coworkee-libretranslate-models` that all apps on the machine share, and lets the services with the localization module wait until it answers `/health`. The first start downloads the models; later starts find them in the volume. It also seeds the settings `TranslationProvider` and `TranslationEndpoint`.

```csharp title="AppHost.cs"
builder.AddCoworkeeApp("myapp", options => options.UseLibreTranslate(["en", "de", "fr", "es", "it"]));
```

A language switched on later that is not in the list is not loaded: add it and restart the app host. Another service is seeded with `UseTranslation`, its key from a parameter:

```csharp title="AppHost.cs"
var key = builder.AddParameter("translation-key", secret: true);
builder.AddCoworkeeApp("myapp", options => options.UseTranslation("deepL", key));
```

Apps of `coworkee new` read `Parameters:translation-key`, `translation-region` and `translation-email` from the user secrets of the app host:

```bash
dotnet user-secrets --project src/MyApp.AppHost set Parameters:translation-key "<key>"
```

Outside the app host, set the settings on the *Settings* page or as defaults in the configuration (`Coworkee:Settings:Defaults:Localization.TranslationProvider`). A production LibreTranslate runs as any container (`libretranslate/libretranslate`); point `TranslationEndpoint` at it.

### CLI and Studio

`coworkee new --languages en,de,fr,es --translation-provider libreTranslate` offers any of the common languages from the start; those without shipped texts are translated at the first start. `--translation-source de` starts from German. Studio has a *Languages* step with a searchable list of languages, the default and source language and the services side by side with what each costs and needs.
