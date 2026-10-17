# AirWorks-Client: Bestandsaufnahme, Konsolidierung, Web-Components-Strategie

Stand 2026-10-17, Basis `@coworkee/airworks` 0.0.1030 (`typescript/airworks`), AirWorks 0.4.0 (`d295c65`), Demo `src/MyApp.AirWorks`.

## 1. Bestandsaufnahme

Ein Admin sieht heute **14 Desktop-Kacheln** und hinter *Administration* weitere **22 Apps**; dazu 3 versteckte (Vorschau, Bericht, Berichtsdesigner).

| App | Was sie tut | Qualität | Überschneidung / Problem |
|---|---|---|---|
| Profile | Vor-/Nachname, Telefon, Konto-Link, Abmelden | dünn (3 Felder) | gehört zu *Einstellungen*; eigene Kachel lohnt nicht |
| Settings | Benutzer/Organisation/System aus den Setting-Definitionen | gut | – |
| Files (+ Preview) | Ordner, Upload/Drop, Umbenennen, Löschen, MudEx-Vorschau im Frame | solide; `prompt()`/`confirm()` des Browsers, Emoji-Icons | – |
| Data | jede OData-Menge als Tabelle (Suche, Sortierung, Paging) | roh: Spaltennamen = Property-Namen, nur Lesen, kein Klick auf eine Zeile, keine Facetten, kein Verlauf, kein Workflow | doppelt mit `entityApp` (Demo: *Brands*) |
| entityApp | eine Menge als eigene App | wie Data | s. o. |
| Chat | Kontakte, Präsenz, Ungelesen, live | gut | Ungelesen nur im Fenster sichtbar |
| Notifications | lesen, gelesen markieren, löschen, live | ok, aber Feed ohne Links/Aktionen | **Freigaben fehlen ganz** (`/api/v1/workflows/approvals`); kein Zähler auf der Kachel |
| Reports / Report / Designer | Liste, Viewer, voller Designer | gut | – |
| Assistant | Sitzungen, Streaming, Tool-Bestätigung | gut | – |
| API documentation | Swagger im iframe | ok | Entwicklerwerkzeug auf jedem Desktop |
| Administration (Hub) + 22 Apps | Identity, System, Kommunikation, Lokalisierung, Monitoring auf `crudApp` | breit, funktional | Audit-Log zeigt nur Änderungen: **kein Rückgängig/Wiederherstellen/Verlauf** wie im Web (0.0.1.30) |
| Demo: Catalog, Product, Sales overview, Brands | Karten, Detailfenster, Facetten-KPIs | gut als Beispiele | *Brands* = Data in klein |

Querschnittsbefunde:
- **Daten sind der schwächste Teil**, obwohl sie der Kern jeder Coworkee-App sind: Die Blazor-App hat Facetten, QueryBuilder, Spaltenfilter, Gruppierung, Verlauf mit Undo, Workflow-Status mit Übergängen, Client-Entities – AirWorks zeigt eine flache Tabelle.
- **Kein „was wartet auf mich“**: Freigaben und ungelesene Nachrichten haben keine Anzeige auf dem Desktop; AirWorks kennt keine Badges.
- **Uneinheitliche Bausteine**: Emoji-Icons (🗑 📁 ⟳), Browser-`prompt`, unterschiedliche leere Zustände (`(empty)`, `Nothing found.`, gar nichts).
- Das API-Snapshot `typescript/client/openapi.json` war hinter dem Lib-Stand (keine Workflows, History, Search) – darum fehlten diese Apps.

## 2. Konsolidierung: wenige starke Apps

| Neu | ersetzt | Inhalt |
|---|---|---|
| **Inbox** | Notifications | Tabs *Freigaben* (Genehmigen/Ablehnen mit Kommentar, Fälligkeit, Link auf den Datensatz) und *Benachrichtigungen*; **Badge** = offene Freigaben + Ungelesene, live |
| **Records** | Data, (entityApp bleibt als Abkürzung) | Mengenliste, Tabelle mit Spaltentiteln aus den Texten; Klick öffnet das **Datensatz-Fenster**: Felder, *Verlauf* (Undo / Zustand davor / danach / gelöschte wiederherstellen, mit Vorschau), *Workflow* (Status, erlaubte Übergänge, Freigabe, Schritte), *Im Web öffnen* |
| **Settings** | Profile, Settings | Tab *Profil* vorn, dann Benutzer/Organisation/System |
| **Administration** | + API documentation | Swagger wandert in die Gruppe *Monitoring*; Audit-Log öffnet den Datensatz mit Verlauf |
| Files, Chat (mit Badge), Reports, Assistant | – | bleiben, Feinschliff |

Ergebnis: **9 Kacheln** (Inbox, Records, Files, Chat, Reports, Assistant, Settings, Administration + App-eigene) statt 14, und jede ist inhaltlich stärker.

Bewusst **nicht** zusammengelegt: Chat in die Inbox – Gespräche brauchen die volle Fensterfläche und sind ein anderer Modus; die Inbox verlinkt aber auf den Chat. Reports bleibt eigenständig (Designer ist groß).

## 3. UX-Grundsätze für Coworkee-AirWorks-Apps

1. **Ein Layout**: Toolbar (Suche links, Aktionen rechts, Primäraktion farbig), Inhalt scrollt, Footer für Paging. Zwei-Spalten-Apps (Liste | Detail) mit fester linker Spalte.
2. **Leere Zustände sagen, was zu tun ist** (`Nothing waits for your decision.`), Fehler stehen am Ort, nicht nur als Toast.
3. **Tastatur**: Suche fokussiert beim Öffnen, Enter öffnet die Zeile, Esc schließt Dialoge (natives `<dialog>`), Tab-Reihenfolge = Lesereihenfolge, `aria-label` an Icon-Buttons.
4. **Theme aus Coworkee**: nur `--cw-*`-Variablen; keine festen Farben außer Fallbacks; Hell/Dunkel aus der Benutzereinstellung.
5. **Status sichtbar ohne Öffnen**: Badges auf Kachel/Startmenü (Inbox, Chat); Live-Aktualisierung über `coworkee.realtime`.
6. **Ein Fenster pro Objekt**: Datensatz, Bericht, Datei öffnen eigene Fenster mit Intent – so funktionieren Suche, Links aus Benachrichtigungen und Wiederherstellen nach Reload.
7. **Keine Doppelentwicklung schwerer Editoren**: was in Blazor tief ist (QueryBuilder, Elsa, Mail-Editor), wird eingebettet, nicht nachgebaut (s. 4).

## 4. Web-Components-Strategie

### Optionen

| | A: Blazor Custom Elements (`RegisterCustomElement`) | B: Einbetten von Coworkee-Seiten (`?embed=1`) | C: TypeScript-Neubau |
|---|---|---|---|
| Treue zur Web-App | voll (gleiche Komponente) | voll (gleiche Seite) | so gut wie nachgebaut |
| Startkosten | .NET-WASM-Runtime je Seite/Frame: ~3–5 MB komprimiert (die MudEx-Komponenten liegen bei 65 MB unkomprimiert inkl. ICU), 1–3 s Kaltstart, danach Cache | dieselben WASM-Kosten, aber die App-Assets sind schon im Browser-Cache, wenn der Nutzer die Web-App kennt | ~0 (wenige kB) |
| CSS-Isolation | **Problem**: MudBlazor-CSS ist global, kein Shadow DOM → muss in ein iframe (so macht es heute die Dateivorschau) | iframe, isoliert | eigenes CSS |
| DI / Dienste | eigenes WASM-Projekt muss `ICoworkeeApi`, Lokalisierung, Theme, Settings, Client-Entities, Realtime registrieren – im Kern eine zweite Client-App | alles vorhanden | Client-Paket |
| Anmeldung | Cookie der AirWorks-BFF (same origin) – geht | Cookie der **Web-App-BFF**: im iframe still per SSO (Auth-Server-Sitzung existiert, `response_mode=query` = 302 ohne Seite, Cookie `SameSite=Strict` ist bei gleicher Site erlaubt). Erstanmeldung ohne Sitzung scheitert an `frame-ancestors 'none'` des Auth-Servers → Hinweis „im Web anmelden“ | AirWorks-BFF |
| Aufwand | hoch (neues Paket, Build, Versionierung, Bundle im AirWorks-Host) | **klein**: Layout-Schalter + URL der Web-App + `embedApp()` | je Bildschirm hoch, und er driftet |
| Pflege | doppelt (Komponente muss ohne Layout/Seite lauffähig sein) | keine | dauerhaft |

### Bewertung

- **A lohnt sich heute nur für Blatt-Komponenten ohne App-Kontext**, und genau das gibt es schon: MudEx-Web-Components (Dateivorschau) im Frame. Für Coworkee-Komponenten (Datentabelle mit Facetten, Bericht, Verlauf, Workflow-Zeitleiste, Chat, Assistent) zieht jede den halben Client mit (Dienste, Texte, Theme, Realtime). Wegen fehlender CSS-Isolation müssten sie ohnehin ins iframe – dann ist B dasselbe ohne den Mehraufwand.
- **B ist der Hebel für schwere Bildschirme**: Sales-Orders mit Facetten/QueryBuilder/Client-Entities, Elsa-Designer, Mail-Template-Editor, Theme-Editor, Workflow-Admin – ein Fenster, volle Treue, null Nachbau. Kosten: ein WASM-Start pro Fenster (Cache warm: ~1 s).
- **C bleibt richtig für leichte, häufige Dinge**, die den Desktop schnell und lebendig machen: Inbox, Freigaben, Benachrichtigungen, Chat, Suche, Datensatz-Fenster mit Verlauf/Workflow (reine REST-Aufrufe), KPI-Kacheln, Dateiliste. Hier wäre jede WASM-Startzeit spürbar.

### Empfehlung (konkreter Pfad)

1. **Jetzt (dieser Durchgang)**: TS-Apps konsolidieren (Inbox, Records + Datensatzfenster, Settings mit Profil, Audit-Aktionen), Badges in AirWorks (generisch, `AirWorks.setBadge`), Embed-Shell als Beweis (B) für einen schweren Bildschirm in der Demo.
2. **Als Nächstes**: `embedApp()` für die schweren Admin-Editoren (Mail-Templates, Themes, Elsa) statt der TS-Nachbauten, die dann entfallen können; *Im Web öffnen* aus jedem Datensatz. Erledigt: Mail-Vorlagen und Themes über `embedOr` (TS-Editoren als Ersatz ohne `appUrl`), Elsa Studio im Browser-Tab (eigener Host, nicht einrahmbar), *In der Web-App öffnen* im Datensatzfenster über die URL des Suchtreffers.
3. **Später, nur bei Bedarf**: Custom Elements (A) für genau eine Komponente, wenn ein Kunde sie ohne Coworkee-Web-App braucht (z. B. Berichtsviewer in einer Fremdseite) – dann als eigenes WASM-Paket mit Token-Übergabe vom Host, im iframe.
4. Embed-Anmeldung härten: Auth-Server erlaubt `frame-ancestors` für registrierte Client-Origins nur auf der Anmeldeseite **oder** Popup-Anmeldung mit `postMessage` zurück ins Fenster. Umgesetzt als Opt-in `Coworkee:Auth:FrameAncestors` (alle Kontoseiten, nicht nur die Anmeldeseite); hilft nur bei gleicher Site, weil Browser Cookies an Frames fremder Sites nicht schicken. Popup bleibt die Lösung für fremde Sites und externe Anbieter.

### Top 5

1. **Inbox mit Freigaben + Badge** – größter Alltagsnutzen, wenig Code.
2. **Records mit Datensatzfenster (Verlauf/Undo, Workflow)** – macht AirWorks vom Viewer zum Arbeitsplatz.
3. **Embed-Shell (`?embed=1` + `embedApp`)** statt Nachbau schwerer Blazor-Bildschirme; Custom Elements nicht weiter verfolgen.
4. **Weniger Kacheln**: Profil in Einstellungen, Swagger in die Administration, Data/entityApp vereinheitlicht.
5. **Einheitliche Bausteine**: Icons statt Emojis, Dialoge statt `prompt/confirm`, leere Zustände, Spaltentitel aus Texten.
