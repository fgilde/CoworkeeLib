---
title: "Lokalisierung und maschinelle Übersetzung für Blazor"
description: "Coworkee liefert Oberfläche, Kontoseiten und Mails in 23 Sprachen, Administratoren schalten Sprachen zur Laufzeit zu, den Rest übersetzt ein Dienst."
---

# Lokalisierung

Die eigenen Seiten zu übersetzen ist der kleinere Teil der Arbeit. Der größere ist alles, was du nicht selbst geschrieben hast: die Tabelle, ihre Filter, die Anmeldeseiten, die Mail mit dem Link zum Zurücksetzen des Passworts.

Coworkee bringt diese Texte in 23 Sprachen mit. Sprachen und Übersetzungen liegen in der Datenbank. Ein Administrator kann also im laufenden Betrieb eine Sprache einschalten oder eine Formulierung korrigieren, und was dann noch fehlt, füllt ein Übersetzungsdienst.

## 23 Sprachen ab Werk

Arabisch, Bulgarisch, Chinesisch (vereinfacht), Dänisch, Deutsch, Englisch, Französisch, Griechisch, Indonesisch, Italienisch, Japanisch, Khmer, Koreanisch, Kroatisch, Niederländisch, Polnisch, Portugiesisch (Brasilien), Russisch, Schwedisch, Spanisch, Tschechisch, Türkisch und Ukrainisch. Das gilt für die Seiten und Komponenten der Module, die Kontoseiten des Auth-Servers und die Mailvorlagen.

Schlüssel jedes Textes ist der englische Wortlaut, eine englische Datei musst du also nicht pflegen; die übrigen 22 Sprachen kommen als Übersetzungsdateien mit. Eine frische Installation bietet Englisch und Deutsch an, alle anderen Sprachen schaltest du auf der Seite *Sprachen* zu. Willst du gleich mit einer anderen Auswahl starten, gib sie der CLI mit:

```bash
coworkee new Shop --languages en,de,fr --default-language de
```

Dieselben Werte landen in `Coworkee:Localization` in der `appsettings.json`. Dort geht jede Kultur, nicht nur die 23. Fehlen ihr die mitgelieferten Texte, wird sie beim ersten Start maschinell übersetzt.

## Texte zur Laufzeit ändern

![Die Seite Sprachen mit nach Sprache gruppierten Kulturen, jede mit Schalter, die Standardsprache mit Stern markiert](../assets/screenshots/languages-light.webp){ .shot loading=lazy }

*Sprachen* zeigt jede Kultur, nach Sprache gruppiert. Schaltest du eine ein, werden ihre fehlenden Texte übersetzt. Schaltest du sie aus, wechseln ihre Benutzer mit einem kurzen Hinweis zur Standardsprache. Offene Browser bekommen die Änderung über die Echtzeitverbindung mit.

*Übersetzungen* zeigt jeden Schlüssel mit dem mitgelieferten und dem bearbeiteten Text je Sprache. Eine Änderung dort sticht die Bibliothek und die JSON-Dateien deiner App aus und kommt ohne Deployment aus. Schlüssel, die eine Seite angefragt hat und für die es keine Übersetzung gibt, meldet der Client; sie tauchen auf derselben Seite auf (praktisch, wenn eine neue Maske schnell fertig werden musste). Für beide Seiten brauchst du `Localization.Manage`.

In deinen eigenen Razor-Komponenten injizierst du `CoworkeeLocalizer` und nimmst den englischen Text als Schlüssel:

```razor
@inject CoworkeeLocalizer L
<MudButton>@L["Save"]</MudButton>
<MudText>@L["Delete {0}? This cannot be undone.", brand.Name]</MudText>
```

Willst du einen Bibliothekstext in der App anders formulieren, kopiert `coworkee texts export --lang de --keys "Filter*"` die passenden Schlüssel in die `Localization/de.json` deiner App. Der Export führt zusammen: Nach einem Update holst du so die neuen Texte, deine Formulierungen bleiben.

## Maschinelle Übersetzung

Ein fehlender Text geht an einen Übersetzungsdienst, sobald die Sprache eingeschaltet wird, nach jedem Start oder wenn ein Administrator auf *Sprachen* die Schaltfläche drückt. Platzhalter wie `{0}` sind geschützt; ein Ergebnis, dem einer fehlt, wird verworfen. Gespeichert wird das Ergebnis wie eine Bearbeitung von Hand, *Übersetzungen* zeigt es also und jeder mit der Berechtigung kann es korrigieren.

| Dienst | Kosten | Hinweis |
|---|---|---|
| LibreTranslate | kostenlos, Open Source | läuft als Container; Standard neuer Apps |
| MyMemory | kostenlos | 5.000 Zeichen am Tag, 50.000 mit Kontakt-E-Mail |
| DeepL | 500.000 Zeichen im Monat frei | Schlüssel mit `:fx` am Ende nutzen die Free-API |
| Azure AI Translator | 2 Millionen Zeichen im Monat frei | über 100 Sprachen |
| Google Cloud Translation | etwa 500.000 Zeichen im Monat frei | Basic (v2) |
| KI | pro Token | Anbieter und Modell des [KI-Assistenten](../modules/ai.md) |

Im Aspire-App-Host ist LibreTranslate eine Zeile. Der Container lädt nur die Modelle der genannten Sprachen und legt sie in einem Volume ab, das sich alle Apps auf dem Rechner teilen (der erste Start lädt sie herunter, das dauert einen Moment):

```csharp title="AppHost.cs"
builder.AddCoworkeeApp("myapp", options => options.UseLibreTranslate(["en", "de", "fr", "es", "it"]));
```

Für die Dienste mit Schlüssel gibt es `UseTranslation`. Ohne App-Host sind das ganz normale Einstellungen wie `Localization.TranslationProvider` und `Localization.TranslatorKey`.

Wer der Bibliothek eine Sprache hinzufügt, lässt `build/localization-check.ps1 -Lang fr` laufen. Das Skript gleicht die Sprache mit den deutschen Dateien ab, bis hin zu Platzhaltern und den Tags in den Mailvorlagen, und endet mit einem Exit-Code ungleich 0, wenn etwas nicht passt.

**Stand:** 21 der 23 Sprachen sind neu und wurden stichprobenartig geprüft, nicht von Muttersprachlern; am unsichersten sind Khmer und Arabisch. Schaltest du später eine Sprache ein, die nicht in der Liste von `UseLibreTranslate` steht, erweitere die Liste und starte den App-Host neu.

Referenz: [Lokalisierung](../modules/localization.md), [Einstellungen der maschinellen Übersetzung](../modules/localization.md#maschinelle-ubersetzung), [`coworkee texts export`](../modules/localization.md#bibliothekstexte-andern).
