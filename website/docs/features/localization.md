---
title: "Localization and machine translation for Blazor"
description: "Coworkee ships its UI, account pages and mails in 23 languages, lets administrators switch languages on at runtime and machine translates the rest."
---

# Localization and machine translation for .NET and Blazor apps

A business app rarely stays in one language. The sales office in Lyon wants French, the warehouse in Gdańsk wants Polish, and nobody wants to wait for a release because a button label is wrong. Translating your own pages is half the work; the other half is every text you did not write yourself: the data grid, the filters, the sign-in pages, the password reset mail.

Coworkee takes care of that second half. The library ships its texts in 23 languages, keeps languages and translations in the database, and lets administrators switch a language on, correct a translation or fill the gaps with a translation service while the app is running.

![The Languages page with cultures grouped by language, each with a switch and the default marked with a star](../assets/screenshots/languages-light.webp){ .shot loading=lazy }

## 23 shipped languages

The texts of the module pages and components, the account pages of the auth server and the mail templates come in Arabic, Bulgarian, Chinese (Simplified), Croatian, Czech, Danish, Dutch, English, French, German, Greek, Indonesian, Italian, Japanese, Khmer, Korean, Polish, Portuguese (Brazil), Russian, Spanish, Swedish, Turkish and Ukrainian.

English is the key of every text, so a new app needs no English file. Out of the box an installation offers English and German; the other shipped languages are one switch away. You can narrow the list in `appsettings.json` or when you create the app:

```json title="appsettings.json"
{
  "Coworkee": {
    "Localization": { "Languages": [ "en", "de" ], "DefaultLanguage": "de" }
  }
}
```

```bash
coworkee new Shop --languages en,de,fr --default-language de
```

Any culture works, not only the shipped 23. A language without shipped texts is machine translated on the first start.

## Live language management

The *Languages* page lists every culture, grouped by language, with a switch. Turning a language on translates the texts it is missing; turning it off moves its users to the default language with a short notice. The change reaches every open browser right away over the realtime connection, so nobody has to reload.

The *Translations* page shows every key with the shipped text and the edited text per language. An edit there wins over the library and over the app's own JSON files, and it needs no deployment. Texts that a page asked for but nobody translated are reported by the client and show up on the same page. Both pages need the `Localization.Manage` permission.

![The Translations page listing keys with their module text and edited text per language](../assets/screenshots/translations-light.webp){ .shot loading=lazy }

## Texts in your own pages

Your module embeds `Localization/{culture}.json` with the English text as key and registers it once. In Razor you inject `CoworkeeLocalizer`:

```razor
@inject CoworkeeLocalizer L
<MudButton>@L["Save"]</MudButton>
<MudText>@L["Delete {0}? This cannot be undone.", brand.Name]</MudText>
```

MudBlazor's own texts (pager, filters, dialogs) are translated too. When you want different wording for a library text, `coworkee texts export` copies it into your app's JSON so you do not have to look up keys:

```bash
coworkee texts export --lang de --keys "Filter*"
coworkee texts export --lang en --keys "/^Delete/"
```

The export merges with what the file already has, so you can run it again after an update and keep your wording.

## Machine translation providers

Texts a language lacks are filled by a translation service: when an administrator switches the language on, with a button on the *Languages* page, and after each start. Placeholders such as `{0}` are protected, and a result that lost one is thrown away. Machine translations are stored like edits, so the *Translations* page shows and corrects them.

| Service | Cost | Notes |
|---|---|---|
| LibreTranslate | free, open source | runs as a container next to your app; default of new apps |
| MyMemory | free | 5,000 characters a day, 50,000 with a contact email |
| DeepL | free plan with 500,000 characters a month | strong for European languages |
| Azure AI Translator | free tier with 2 million characters a month | over 100 languages |
| Google Cloud Translation | about 500,000 characters a month free | Basic (v2) API |
| AI | per token | uses the provider and model of the [AI assistant](../modules/ai.md) |

In the Aspire app host, LibreTranslate is one line. It loads only the models of the languages you list and keeps them in a volume that all apps on the machine share:

```csharp title="AppHost.cs"
builder.AddCoworkeeApp("myapp", options => options.UseLibreTranslate(["en", "de", "fr", "es", "it"]));
```

Another service is seeded with `UseTranslation`, its key coming from an Aspire parameter. Outside the app host the same values are ordinary settings (`Localization.TranslationProvider`, `Localization.TranslatorKey` and friends) on the *Settings* page.

## Checking a language

When you add or edit a shipped language in the repository, `build/localization-check.ps1 -Lang fr` compares it with the German files of every text source: missing and extra keys, `{0}` placeholders, and the expressions and tags of the mail templates. It ends with an exit code other than 0 when something is off, so it fits into a build.

## Where to go next

- [Localization](../modules/localization.md): resource registration, the three text layers, client setup and every translation setting
- [Machine translation](../modules/localization.md#machine-translation): the services in detail, quotas and the app host options
- [Changing library texts](../modules/localization.md#changing-library-texts): `coworkee texts export` with all switches
- [CLI](../getting-started/cli.md): `--languages`, `--default-language` and `--translation-provider` of `coworkee new`
