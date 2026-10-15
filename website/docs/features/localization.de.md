---
title: "Lokalisierung und maschinelle Übersetzung für Blazor"
description: "Coworkee liefert Oberfläche, Kontoseiten und Mails in 23 Sprachen, Administratoren schalten Sprachen zur Laufzeit zu, den Rest übersetzt ein Dienst."
---

# Lokalisierung und maschinelle Übersetzung für .NET- und Blazor-Apps

Eine Geschäftsanwendung bleibt selten bei einer Sprache. Das Vertriebsbüro in Lyon möchte Französisch, das Lager in Danzig Polnisch, und niemand möchte auf ein Release warten, nur weil eine Schaltfläche falsch beschriftet ist. Die eigenen Seiten zu übersetzen ist die eine Hälfte der Arbeit. Die andere Hälfte sind all die Texte, die Sie nicht selbst geschrieben haben: die Tabelle, die Filter, die Anmeldeseiten, die Mail zum Zurücksetzen des Passworts.

Diese zweite Hälfte übernimmt Coworkee. Die Bibliothek bringt ihre Texte in 23 Sprachen mit, hält Sprachen und Übersetzungen in der Datenbank und lässt Administratoren im laufenden Betrieb eine Sprache einschalten, eine Übersetzung korrigieren oder Lücken von einem Übersetzungsdienst füllen.

![Die Seite Sprachen mit nach Sprache gruppierten Kulturen, jede mit Schalter, die Standardsprache mit Stern markiert](../assets/screenshots/languages-light.webp){ .shot loading=lazy }

## 23 mitgelieferte Sprachen

Die Texte der Modulseiten und Komponenten, der Kontoseiten des Auth-Servers und der Mailvorlagen gibt es auf Arabisch, Bulgarisch, Chinesisch (vereinfacht), Dänisch, Deutsch, Englisch, Französisch, Griechisch, Indonesisch, Italienisch, Japanisch, Khmer, Koreanisch, Kroatisch, Niederländisch, Polnisch, Portugiesisch (Brasilien), Russisch, Schwedisch, Spanisch, Tschechisch, Türkisch und Ukrainisch.

Englisch ist der Schlüssel jedes Textes, eine neue App braucht also keine englische Datei. Ohne Konfiguration bietet eine Installation Englisch und Deutsch an; die übrigen mitgelieferten Sprachen sind einen Schalter entfernt. Die Auswahl lässt sich in `appsettings.json` oder beim Anlegen der App einschränken:

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

Jede Kultur funktioniert, nicht nur die 23 mitgelieferten. Eine Sprache ohne mitgelieferte Texte wird beim ersten Start maschinell übersetzt.

## Sprachen zur Laufzeit verwalten

Die Seite *Sprachen* zeigt jede Kultur, nach Sprache gruppiert, mit einem Schalter. Wird eine Sprache eingeschaltet, werden ihre fehlenden Texte übersetzt; wird sie ausgeschaltet, wechseln ihre Benutzer mit einem kurzen Hinweis zur Standardsprache. Die Änderung erreicht über die Echtzeitverbindung sofort jeden offenen Browser, niemand muss neu laden.

Die Seite *Übersetzungen* zeigt jeden Schlüssel mit dem mitgelieferten und dem bearbeiteten Text je Sprache. Eine Änderung dort hat Vorrang vor der Bibliothek und vor den JSON-Dateien der App und braucht kein Deployment. Texte, die eine Seite angefragt hat, für die es aber keine Übersetzung gibt, meldet der Client; sie erscheinen auf derselben Seite. Beide Seiten erfordern die Berechtigung `Localization.Manage`.

![Die Seite Übersetzungen mit Schlüsseln, Modultext und bearbeitetem Text je Sprache](../assets/screenshots/translations-light.webp){ .shot loading=lazy }

## Texte in eigenen Seiten

Ihr Modul bettet `Localization/{culture}.json` mit dem englischen Text als Schlüssel ein und registriert die Datei einmal. In Razor injizieren Sie `CoworkeeLocalizer`:

```razor
@inject CoworkeeLocalizer L
<MudButton>@L["Save"]</MudButton>
<MudText>@L["Delete {0}? This cannot be undone.", brand.Name]</MudText>
```

Auch die Texte von MudBlazor selbst (Pager, Filter, Dialoge) werden übersetzt. Wenn Sie einen Bibliothekstext anders formulieren möchten, kopiert `coworkee texts export` ihn in die JSON-Datei Ihrer App, ohne dass Sie Schlüssel nachschlagen müssen:

```bash
coworkee texts export --lang de --keys "Filter*"
coworkee texts export --lang en --keys "/^Delete/"
```

Der Export führt mit dem vorhandenen Inhalt zusammen. Sie können ihn nach einem Update erneut ausführen, Ihre Formulierungen bleiben erhalten.

## Dienste für maschinelle Übersetzung

Texte, die einer Sprache fehlen, füllt ein Übersetzungsdienst: wenn ein Administrator die Sprache einschaltet, per Schaltfläche auf der Seite *Sprachen* und nach jedem Start. Platzhalter wie `{0}` werden geschützt, ein Ergebnis, das einen verloren hat, wird verworfen. Maschinelle Übersetzungen werden wie Bearbeitungen gespeichert, die Seite *Übersetzungen* zeigt und korrigiert sie also.

| Dienst | Kosten | Hinweise |
|---|---|---|
| LibreTranslate | kostenlos, Open Source | läuft als Container neben der App; Standard neuer Apps |
| MyMemory | kostenlos | 5.000 Zeichen am Tag, 50.000 mit Kontakt-E-Mail |
| DeepL | Free-Plan mit 500.000 Zeichen im Monat | stark bei europäischen Sprachen |
| Azure AI Translator | kostenloses Kontingent mit 2 Millionen Zeichen im Monat | über 100 Sprachen |
| Google Cloud Translation | etwa 500.000 Zeichen im Monat kostenlos | Basic-API (v2) |
| KI | pro Token | nutzt Anbieter und Modell des [KI-Assistenten](../modules/ai.md) |

Im Aspire-App-Host ist LibreTranslate eine Zeile. Der Container lädt nur die Modelle der angegebenen Sprachen und legt sie in einem Volume ab, das sich alle Apps auf dem Rechner teilen:

```csharp title="AppHost.cs"
builder.AddCoworkeeApp("myapp", options => options.UseLibreTranslate(["en", "de", "fr", "es", "it"]));
```

Einen anderen Dienst richtet `UseTranslation` ein, den Schlüssel aus einem Aspire-Parameter. Außerhalb des App-Hosts sind dieselben Werte gewöhnliche Einstellungen (`Localization.TranslationProvider`, `Localization.TranslatorKey` und weitere) auf der Seite *Einstellungen*.

## Eine Sprache prüfen

Wer im Repository eine mitgelieferte Sprache hinzufügt oder bearbeitet, vergleicht sie mit `build/localization-check.ps1 -Lang fr` gegen die deutschen Dateien jeder Textquelle: fehlende und überzählige Schlüssel, `{0}`-Platzhalter sowie Ausdrücke und Tags der Mailvorlagen. Findet das Skript etwas, endet es mit einem Exit-Code ungleich 0 und passt damit in einen Build.

## Weiterlesen

- [Lokalisierung](../modules/localization.md): Registrierung der Ressourcen, die drei Textebenen, Einrichtung im Client und alle Übersetzungseinstellungen
- [Maschinelle Übersetzung](../modules/localization.md#maschinelle-ubersetzung): die Dienste im Detail, Kontingente und die Optionen im App-Host
- [Bibliothekstexte ändern](../modules/localization.md#bibliothekstexte-andern): `coworkee texts export` mit allen Schaltern
- [CLI](../getting-started/cli.md): `--languages`, `--default-language` und `--translation-provider` von `coworkee new`
