# Lokalisierung

`Coworkee.Localization` hält Sprachen und Übersetzungen in der Datenbank und liefert die Texte jedes Moduls als eingebettetes JSON mit.

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

Schlüssel sind die englischen Texte. Ein Modul bettet `Localization/{culture}.json` ein:

```json title="Localization/de.json"
{
  "Brands": "Marken",
  "Delete {0}? This cannot be undone.": "{0} löschen? Das lässt sich nicht rückgängig machen."
}
```

```xml
<EmbeddedResource Include="Localization\*.json" />
```

## Anfangssprachen

Die Bibliothek bringt 23 Sprachen mit (`ar`, `bg`, `cs`, `da`, `de`, `el`, `en`, `es`, `fr`, `hr`, `id`, `it`, `ja`, `km`, `ko`, `nl`, `pl`, `pt`, `ru`, `sv`, `tr`, `uk`, `zh`), Englisch sind die Schlüssel. Ohne Konfiguration bietet eine Installation Englisch und Deutsch an, Englisch als Standard; die übrigen mitgelieferten Sprachen schalten Administratoren auf der Seite *Sprachen* ein. `LocalizationOptions` (Abschnitt `Coworkee:Localization`) schränken das ein; das Setup speichert genau diese Sprachen, danach ändern Administratoren sie auf der Seite *Sprachen*.

```json title="appsettings.json"
{
  "Coworkee": {
    "Localization": { "Languages": [ "en", "de" ], "DefaultLanguage": "de" }
  }
}
```

Jede Kultur geht, nicht nur die mitgelieferten: Sprachen ohne mitgelieferte Texte werden [maschinell übersetzt](#maschinelle-ubersetzung). Mit einer unbekannten Kultur oder einer Standardsprache, die nicht unter den Sprachen ist, startet der Host nicht. `coworkee new --languages en,de,fr --default-language de` schreibt dieselben Werte in eine neue App.

## Im Client

`CoworkeeLocalizer` übersetzt in Seiten und Komponenten; fehlende Schlüssel fallen auf den Schlüssel zurück und werden an den Server gemeldet, wo sie unter *Übersetzungen* auftauchen.

```razor
@inject CoworkeeLocalizer L
<MudButton>@L["Save"]</MudButton>
<MudText>@L["Delete {0}? This cannot be undone.", brand.Name]</MudText>
```

Die Sprache wählt der Benutzer in der App-Leiste. Auch die Texte von MudBlazor (Pager, Filter, Dialoge) werden übersetzt. Die Kultur wird vor dem ersten Rendern gesetzt:

```csharp title="Program.cs"
var host = builder.Build();
await host.Services.InitializeCoworkeeClientAsync();
await host.RunAsync();
```

## Verwaltung

- *Sprachen* (`/admin/languages`): alle Kulturen, nach Sprache gruppiert, mit einem Schalter. Beim Einschalten werden fehlende Texte mit dem [Übersetzungsdienst](#maschinelle-ubersetzung) der Einstellungen übersetzt; ein Knopf übersetzt später noch Fehlendes. Der Stern markiert die Standardsprache, die sich nicht abschalten lässt.

![Sprachen](../assets/screenshots/languages.png){ .shot }
- *Übersetzungen* (`/admin/translations`): jeder Schlüssel mit Modultext und bearbeitetem Text je Sprache. Bearbeitungen gewinnen gegen die Modultexte.

Beides braucht `Localization.Manage`.

Jede Änderung erreicht alle Clients sofort (Realtime-Topic `global:localization`): sie laden ihre Texte neu, und Benutzer, deren Sprache abgeschaltet wurde, wechseln mit kurzem Hinweis zur Standardsprache.

## Maschinelle Übersetzung

Texte, die einer Sprache fehlen, werden maschinell übersetzt: wenn ein Administrator die Sprache einschaltet, mit dem Knopf auf der Seite *Sprachen* und nach jedem Start für die eingeschalteten Sprachen (eine Sprache aus `LocalizationOptions` ohne mitgelieferte Texte oder die neuen Texte eines Updates). Übersetzungen werden wie Bearbeitungen gespeichert, die Seite *Übersetzungen* zeigt und korrigiert sie also; die mitgelieferten Texte bleiben maßgeblich. Platzhalter wie `{0}` bleiben von der Übersetzung ausgenommen, und eine Übersetzung, die einen verloren hat, wird verworfen. Ein Dienst, der scheitert (aufgebrauchtes Kontingent, Server nicht erreichbar), beendet den Lauf mit dem, was er bis dahin übersetzt hat; der Rest folgt beim nächsten Start oder mit dem Knopf.

| Dienst | Kosten | Konto | Wohin die Texte gehen | Gut zu wissen |
|---|---|---|---|---|
| `libreTranslate` | kostenlos, Open Source (AGPL) | keines | eigener Server oder Container | Image von etwa 600 MB plus etwa 100 MB je Sprache; gute Qualität für die großen Sprachen; Standard neuer Apps |
| `myMemory` | kostenlos | keines, optional Kontakt-E-Mail | öffentlicher Dienst von Translated | 5.000 Zeichen am Tag je Adresse, 50.000 mit E-Mail; ein Text von bis zu 500 Byte je Anfrage. Die Bibliothekstexte einer Sprache haben etwa 20.000 Zeichen, eine ganze Sprache dauert also Tage: gut für einzelne neue Texte |
| `deepL` | kostenloser Tarif 500.000 Zeichen im Monat | Schlüssel | DeepL | Schlüssel mit der Endung `:fx` nutzen `api-free.deepl.com`; beste Qualität für europäische Sprachen |
| `azure` | kostenlose Stufe 2 Millionen Zeichen im Monat | Schlüssel und Region | Microsoft | über 100 Sprachen |
| `google` | etwa 500.000 Zeichen im Monat kostenlos | Schlüssel | Google | Cloud Translation Basic (v2) |
| `ai` | je Token des Modells | der [KI-Assistent](ai.md) eingerichtet | der Anbieter des Modells | natürlicher Wortlaut; nutzt Anbieter, Modell und Schlüssel der KI-Einstellungen, auch bei abgeschaltetem Assistenten |
| `none` | | | | fehlende Texte erscheinen auf Englisch |

Inoffizielle Endpunkte wie die kostenlose Google-Adresse, die Browser-Erweiterungen und manche Kopfhörer-Apps aufrufen (`translate.googleapis.com` mit `client=gtx`), sind bewusst nicht dabei: Sie haben keine Nutzungsbedingungen für Dritte, sperren oder drosseln ohne Ankündigung und können sich jederzeit ändern.

### Einstellungen

Die Einstellungsgruppe *Übersetzung* (nur global, `Localization.*`):

| Einstellung | Bedeutung |
|---|---|
| `Localization.TranslationProvider` | Einer der Dienste oben; Standard `azure`, das ohne Schlüssel nichts tut |
| `Localization.TranslatorKey` | Schlüssel von DeepL, Azure oder Google, der optionale API-Schlüssel eines LibreTranslate-Servers; ein Geheimnis, im Formular maskiert |
| `Localization.TranslatorRegion` | Region der Azure-Übersetzer-Ressource |
| `Localization.TranslationEndpoint` | Adresse des LibreTranslate-Servers; für die anderen ein optionaler Ersatz der öffentlichen Adresse (ein Proxy) |
| `Localization.TranslationEmail` | Kontakt für MyMemory, hebt das Kontingent auf 50.000 Zeichen am Tag |
| `Localization.TranslationSource` | Die Sprache, von der aus übersetzt wird, Standard `en` |
| `Localization.AutoTranslate` | Beim Einschalten einer Sprache und nach dem Start übersetzen (Standard `true`); aus, übersetzt nur der Knopf |

Texte sind nach ihrem englischen Wortlaut geschlüsselt, Englisch ist also die natürliche Ausgangssprache. Mit `TranslationSource` auf `de` geht ein Text von seinem deutschen Wortlaut aus, wenn es einen gibt (mitgeliefert oder bearbeitet), sonst vom englischen; eine Sprache übersetzt nie aus sich selbst, `de-CH` geht also vom Englischen aus.

### Im AppHost

`UseLibreTranslate` fügt einen LibreTranslate-Container hinzu, der nur die Modelle der angegebenen Sprachen und der Ausgangssprache lädt (`LT_LOAD_ONLY`, Englisch ist immer dabei), sie im Volume `coworkee-libretranslate-models` hält, das alle Apps auf dem Rechner teilen, und die Dienste mit dem Lokalisierungsmodul warten lässt, bis er auf `/health` antwortet. Der erste Start lädt die Modelle herunter; spätere Starts finden sie im Volume. Außerdem setzt er die Einstellungen `TranslationProvider` und `TranslationEndpoint`.

```csharp title="AppHost.cs"
builder.AddCoworkeeApp("myapp", options => options.UseLibreTranslate(["en", "de", "fr", "es", "it"]));
```

Eine später eingeschaltete Sprache, die nicht in der Liste steht, wird nicht geladen: ergänzen Sie sie und starten Sie den AppHost neu. Einen anderen Dienst setzt `UseTranslation`, seinen Schlüssel aus einem Parameter:

```csharp title="AppHost.cs"
var key = builder.AddParameter("translation-key", secret: true);
builder.AddCoworkeeApp("myapp", options => options.UseTranslation("deepL", key));
```

Apps von `coworkee new` lesen `Parameters:translation-key`, `translation-region` und `translation-email` aus den User Secrets des AppHosts:

```bash
dotnet user-secrets --project src/MyApp.AppHost set Parameters:translation-key "<key>"
```

Außerhalb des AppHosts setzen Sie die Einstellungen auf der Seite *Einstellungen* oder als Standardwerte in der Konfiguration (`Coworkee:Settings:Defaults:Localization.TranslationProvider`). Ein LibreTranslate für den Betrieb läuft wie jeder Container (`libretranslate/libretranslate`); `TranslationEndpoint` zeigt darauf.

### CLI und Studio

`coworkee new --languages en,de,fr,es --translation-provider libreTranslate` bietet jede der gängigen Sprachen von Anfang an; die ohne mitgelieferte Texte werden beim ersten Start übersetzt. `--translation-source de` geht vom Deutschen aus. Studio hat einen Schritt *Sprachen* mit einer durchsuchbaren Sprachliste, Standard- und Ausgangssprache und den Diensten nebeneinander, mit dem, was jeder kostet und braucht.
