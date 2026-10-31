---
title: "Localization and machine translation for Blazor"
description: "Coworkee ships its UI, account pages and mails in 23 languages, lets administrators switch languages on at runtime and machine translates the rest."
---

# Localization

Translating your own pages is the smaller part of the job. The larger part is everything you did not write: the data grid, its filters, the sign-in pages, the mail with the password reset link.

Coworkee ships those texts in 23 languages. Languages and translations live in the database, so an administrator can switch a language on or fix a wording while the app is running, and a translation service fills whatever is still missing.

## 23 languages in the box

Arabic, Bulgarian, Chinese (Simplified), Croatian, Czech, Danish, Dutch, English, French, German, Greek, Indonesian, Italian, Japanese, Khmer, Korean, Polish, Portuguese (Brazil), Russian, Spanish, Swedish, Turkish and Ukrainian. That covers the module pages and components, the account pages of the auth server and the mail templates.

English is the key of every text, so there is no English file to maintain; the other 22 languages ship as translation files. A fresh installation offers English and German; the rest is one switch away on the *Languages* page. To start with a different set, pass it to the CLI:

```bash
coworkee new Shop --languages en,de,fr --default-language de
```

The same values end up in `Coworkee:Localization` in `appsettings.json`. Any culture works there, not only the 23. One without shipped texts gets machine translated on the first start.

## Changing texts at runtime

![The Languages page with cultures grouped by language, each with a switch and the default marked with a star](../assets/screenshots/languages-light.webp){ .shot loading=lazy }

*Languages* lists every culture, grouped by language. Switch one on and its missing texts are translated; switch it off and its users move to the default language with a short notice. Open browsers pick the change up over the realtime connection.

*Translations* shows every key with the shipped text and the edited one per language. An edit there beats both the library and your app's JSON files and needs no deployment. Keys a page asked for that nobody translated are reported by the client and land on the same page, which is handy after adding a screen in a hurry. Both pages need `Localization.Manage`.

In your own Razor components you inject `CoworkeeLocalizer` and use the English text as key:

```razor
@inject CoworkeeLocalizer L
<MudButton>@L["Save"]</MudButton>
<MudText>@L["Delete {0}? This cannot be undone.", brand.Name]</MudText>
```

To reword a library text in the app itself, `coworkee texts export --lang de --keys "Filter*"` copies the matching keys into your app's `Localization/de.json`. It merges, so running it again after an update keeps your wording.

## Machine translation

A missing text goes to a translation service when the language is switched on, after each start, or when an administrator presses the button on *Languages*. Placeholders such as `{0}` are protected; a result that lost one is dropped. The output is stored like a manual edit, so *Translations* shows it and anyone with the permission can correct it.

| Service | Cost | Note |
|---|---|---|
| LibreTranslate | free, open source | runs as a container; default of new apps |
| MyMemory | free | 5,000 characters a day, 50,000 with a contact email |
| DeepL | 500,000 characters a month free | keys ending in `:fx` use the free API |
| Azure AI Translator | 2 million characters a month free | over 100 languages |
| Google Cloud Translation | about 500,000 characters a month free | Basic (v2) |
| AI | per token | provider and model of the [AI assistant](../modules/ai.md) |

In the Aspire app host LibreTranslate takes one line. It loads only the models of the listed languages and keeps them in a volume shared by every app on the machine (the first start downloads them, so give it a minute):

```csharp title="AppHost.cs"
builder.AddCoworkeeApp("myapp", options => options.UseLibreTranslate(["en", "de", "fr", "es", "it"]));
```

For the keyed services there is `UseTranslation`. Outside the app host they are plain settings such as `Localization.TranslationProvider` and `Localization.TranslatorKey`.

Contributors adding a language to the library run `build/localization-check.ps1 -Lang fr`. It compares the language with the German files, down to placeholders and the tags in mail templates, and exits non-zero when something is off.

**Status:** 21 of the 23 languages are recent and were checked by sample, not by native speakers; Khmer and Arabic are the least certain. A language switched on later that is not in the `UseLibreTranslate` list needs the list extended and the app host restarted.

Reference: [Localization](../modules/localization.md), [machine translation settings](../modules/localization.md#machine-translation), [`coworkee texts export`](../modules/localization.md#changing-library-texts).
