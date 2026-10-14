# AirWorks-Client

Eine Coworkee-App kann neben der Blazor-App einen zweiten Client haben: einen Web-Desktop auf Basis von [AirWorks](https://fgilde.github.io/AirWorks/de.html), mit Fenstern, Taskleiste, mehreren Desktops, Ordnern, Suche und Pop-outs. Er meldet sich über denselben Auth-Server an (und damit über denselben Keycloak-Realm), spricht mit derselben API, zeigt Logo und Theme der App und bringt fast alles mit, was die Blazor-App bietet: Einstellungen, Profil, Dateien mit Vorschau, Datentabellen, Chat, Berichte, den KI-Assistenten, Benachrichtigungen und die Administration (Benutzer, Gruppen, Rollen, Anwendungen, Scopes, API-Schlüssel, Themes, Sprachen, Übersetzungen, Mail-Vorlagen, Kampagnen, Empfängerlisten, Mail-Protokoll, Mail- und KI-Einstellungen, Backups, Mandanten, Editionen, Audit-Log). Die Bibliothek liefert die Apps als npm-Pakete; die App setzt sie zusammen und ergänzt eigene.

## Eine App damit erstellen

```bash
coworkee new Shop --airworks
```

In Coworkee Studio ist es die Karte *AirWorks-Client* unter *Allgemein*, mit einem Link zur AirWorks-Dokumentation. Die Solution bekommt das Projekt `Shop.AirWorks` und der AppHost eine Zeile:

```csharp
app.AddWeb<Projects.Shop_Web>();
app.AddAirWorks<Projects.Shop_AirWorks>();
```

Starten Sie den AppHost wie gewohnt und öffnen Sie *shop-airworks* aus dem Aspire-Dashboard. Zum Bauen braucht das Projekt Node.js 22.18 oder neuer: `dotnet build` führt `npm install` und `npm run build` in `ClientApp` aus (überspringen mit `-p:SkipClientBuild=true`).

## So meldet er sich an

`Shop.AirWorks` ist ein kleiner ASP.NET-Core-Host mit [Coworkee.Bff](../security/clients.md), genau wie der Web-Host der Blazor-App:

- `AddAirWorks` registriert ihn als zweiten Client am Auth-Server (`shop-airworks`, Redirect `/signin-oidc`, Scope `shop_api`) und richtet sein BFF auf die erste API aus.
- Die Anmeldung geht zum Auth-Server, der sein eigenes Formular und Keycloak anbietet, wie es der [Anmeldemodus](../security/authentication.md) vorgibt. Benutzer, Rollen und Berechtigungen sind die der Blazor-App.
- Die Tokens bleiben auf dem Server. Der Browser hält ein Sitzungs-Cookie namens `shop-airworks` (Browser senden Cookies an jeden Port von `localhost`, deshalb brauchen die beiden Clients verschiedene Namen) und schickt bei jeder Änderung `X-CSRF: 1` mit.
- `/api`, `/odata`, `/hubs`, `/openapi` und `/swagger` werden mit dem Access-Token an die API weitergeleitet; so läuft auch der Realtime-Hub über dieselbe Sitzung.

## Die Client-App

`ClientApp/src/main.ts` gehört der App:

```ts
import { adminApps, defaultApps, entityApp, useCoworkee } from '@coworkee/airworks';
import { AirWorks } from '@fgilde/airworks';
import '@fgilde/airworks/style.css';
import '@coworkee/airworks/style.css';
import logo from '../../../Shop.Web.Client/wwwroot/coworkee-icon.svg?url';

const coworkee = await useCoworkee(AirWorks, { title: 'Shop', logo });
await coworkee.register(
  ...defaultApps,
  ...adminApps,
  entityApp({ entitySet: 'Products', liveType: 'Product', icon: 'ratio-product.png', requiredPermissions: ['Catalog.Products.View'] }),
);
document.body.append(document.createElement('air-desktop'));
```

`useCoworkee` liest den Benutzer vom BFF, das Theme von `/api/v1/themes/current` (Farben des Farbmodus des Benutzers als CSS-Variablen `--cw-primary`, `--cw-surface`, `--cw-text`, ..., die Primärfarbe als Systemfarbe von AirWorks, das Logo des Themes oder das übergebene), die Client-Einstellungen und die Texte der Sprache des Benutzers, registriert Anmeldung und Berechtigungen (`/api/v1/identity/permissions/me`) bei AirWorks und konfiguriert es. Optionen von AirWorks selbst (Hintergründe, `initialShortcuts`, `initialApps`, ...) gehören in `airWorks: { ... }`.

### Apps für alle Benutzer (`defaultApps`)

| App | Id | Sichtbar, wenn |
|---|---|---|
| Profil: Name, Telefon, Kontoseiten, Abmelden | `coworkee.profile` | immer |
| Einstellungen: die des Benutzers, der Organisation und des Systems, aus den Einstellungsdefinitionen | `coworkee.settings` | immer; Organisation und System mit `Settings.Manage` |
| Dateien: Ordner, Hochladen (auch per Drag & Drop), Umbenennen, Löschen, Herunterladen | `coworkee.files` | `Files.View` |
| Dateivorschau mit der MudEx-Dateianzeige (PDF, Bilder, Office, Archive, Markdown, Audio, Video) | `coworkee.file-preview` | aus Dateien geöffnet |
| Daten: jedes OData-Entity-Set als blätterbare, sortierbare und durchsuchbare Tabelle | `coworkee.data` | die App hat OData |
| Chat: Personen der Organisation mit Anwesenheit und ungelesenen Nachrichten, Nachrichten kommen live an | `coworkee.chat` | `Chat.Use` und das Chat-Modul |
| Berichte: Liste, Favoriten, Duplizieren, Löschen, ein Bericht mit KPIs, Diagrammen und Tabellen und der Berichtsdesigner | `coworkee.reports`, `coworkee.report`, `coworkee.report-designer` | `Reports.View` und das Reporting-Modul |
| Assistent: Unterhaltungen, Antworten erscheinen beim Schreiben, Bestätigung von Tools, die Daten ändern | `coworkee.assistant` | `Ai.Chat` und das KI-Modul |
| Benachrichtigungen: lesen, als gelesen markieren, löschen; neue erscheinen zusätzlich als Hinweis | `coworkee.notifications` | die App hat das Notifications-Modul |
| API-Dokumentation (Swagger UI) | `coworkee.api-docs` | `ApiDocs.View` |
| `entityApp({ entitySet, ... })`: ein Entity-Set als eigene App; `liveType` lädt bei Änderungen neu | `coworkee.entity.{set}` | ihre `requiredPermissions` |

### Administration (`adminApps`)

Die Administrations-Apps bekommen keine eigene Desktop-Verknüpfung: Die App *Administration* listet sie gruppiert wie das Administrationsmenü der Web-App und erscheint nur, wenn der Benutzer mindestens eine davon öffnen darf. Jede braucht die Berechtigung ihrer Seite in der Blazor-App; Apps mit *System* erscheinen nur für Benutzer der System-Organisation, wie die Host-Seiten.

| Bereich | Apps (Berechtigung) |
|---|---|
| Identität | Benutzer (`Identity.Users.View`; bearbeiten, Rollen, Gruppen, Berechtigungen, Passwort, sperren, Reset-Mail, überall abmelden), Gruppen (`Identity.Groups.View`), Rollen mit ihren Berechtigungen (`Identity.Roles.View`), Anwendungen und Scopes (`Identity.Clients.Manage`, System), API-Schlüssel (`ApiKeys.Manage`) |
| System | Themes (`Themes.Manage`; neu aus einem eingebauten Theme, Paletten, Logo, CSS, Standard, Keycloak-Anmeldeseite), Backups, Dienste, Editionen, Mandanten mit ihren Features (System), KI-Einstellungen mit Verbindungstest (System) |
| Kommunikation | Mail-Vorlagen mit Live-Vorschau und Testmail (`Mail.Templates.Manage`), Mail-Kampagnen mit Empfängervorschau, Test, Planen und Abbrechen, Empfängerlisten (`Mail.Campaigns.Send`), Mail-Protokoll (`Mail.Log.View`), Mail-Einstellungen mit Verbindungstest (`Settings.Manage`) |
| Lokalisierung | Sprachen (ein-/ausschalten, Standard), Übersetzungen mit maschineller Übersetzung der fehlenden Texte (`Localization.Manage`) |
| Überwachung | Audit-Log mit den geänderten Feldern (`Audit.View`), KI-Tool-Aufrufe (`Ai.Audit`) |

Apps lassen Sie weg, indem Sie nur die gewünschten registrieren; ersetzen Sie eine, indem Sie nach `defaultApps` eine eigene App mit derselben Id registrieren. Eigene Apps sind normale AirWorks-Apps (siehe [AirWorks-Dokumentation](https://fgilde.github.io/AirWorks/de.html)); eine Factory bekommt das `coworkee`-Objekt mit dem typisierten API-Client, `request`, dem Realtime-Hub, der Übersetzungsfunktion `t`, dem Benutzer, dem Theme und `notify`:

```ts
const ordersApp = (coworkee: Coworkee): CoworkeeApp => ({
  id: 'shop.orders', title: coworkee.t('Orders'), icon: 'fn-planning.png', requiredPermissions: ['Orders.View'],
  render: async ({ host }) => {
    const orders = await coworkee.request<Order[]>('/api/v1/orders');
    host.innerHTML = `<div class="cw-app">${orders.length} orders</div>`;
  },
});
await coworkee.register(...defaultApps, ordersApp);
```

Listen mit Anlegen, Bearbeiten, Löschen und Zeilenaktionen sind ein Aufruf: `crudApp({ id, title, icon, permissions, load, columns, create, edit, remove, actions })` (die Administrations-Apps sind damit gebaut); `formDialog`, `confirmDialog` und `infoDialog` sind die Dialoge dazu.

### Realtime

`coworkee.realtime` verbindet sich beim ersten Abonnement mit dem Realtime-Hub der App (SignalR über das BFF) und abonniert nach einem Wiederverbinden alle gefolgten Topics erneut:

```ts
const stop = coworkee.realtime.subscribe('type:Order', ({ type, payload }) => reload());
```

Der Desktop selbst folgt dem Topic des Benutzers (neue Benachrichtigungen erscheinen als Hinweis, eine anderswo beendete Sitzung meldet ab) und den Berechtigungs-Topics (geänderte Rollen oder Rechte blenden Apps sofort ein und aus). Chat und Anwesenheit, die Benachrichtigungs-App und `entityApp({ liveType })` folgen ihren Topics, solange ihr Fenster offen ist.

### Berichtsdesigner

*Neuer Bericht* in Berichte und *Bearbeiten* in einem geöffneten Bericht (für alle, die ihn ändern dürfen, `Reports.Manage`) öffnen den Designer: die Widget-Palette der Web-App (KPI, Balken, horizontal, gestapelt, Linie, Fläche, Kreis, Ring, Streuung, Netz, Treemap, Heatmap, Tabelle, Datentabelle, Pivot, Text, Bild, Webseite), Widgets auf dem 12-Spalten-Raster (Titel ziehen zum Verschieben, Ecke zum Vergrößern), ein Seitenbereich je Widget mit Datenquelle, Gruppierung und Serien (mit Zeiteinheiten), Werten (Anzahl, Summe, Durchschnitt, Minimum, Maximum, Anzahl verschiedener), Spalten und Sortierung, Top, dem Datumsfeld für den Zeitraum des Berichts, einem OData-Filter und der Darstellung (Zahlenformat, Währung, Zielwert, Vergleich, Verlauf, Legende, Werte im Diagramm, Kreuzfilter, Tabellenansicht, Markdown-Text, Adresse von Bildern und Webseiten). Die Berichtseinstellungen enthalten Name, Beschreibung, Symbol, automatische Aktualisierung und Freigabe (nur ich, Rollen, Organisation; mit `Reports.Share`); Rückgängig und Wiederholen gelten für jede Änderung. Ansicht und Designer wenden den Zeitraum des Berichts auf jedes Widget mit Datumsfeld an.

### Auswahlfelder

Felder, die Benutzer oder Gruppen auswählen (Gruppenmitglieder, die Gruppen eines Benutzers, die Empfänger einer Mail-Kampagne), suchen auf dem Server und blättern durch die Ergebnisse (`type: 'lookup'` in `formDialog`, `userLookup`/`groupLookup`); so funktionieren sie mit beliebig vielen Benutzern.

### Dateivorschau

Die Dateivorschau nutzt die [Web-Komponenten von MudBlazor.Extensions](https://www.mudex.org) in einem eigenen Frame (`mudExFrame`): Die Seitenstyles von MudBlazor bleiben im Frame, Desktop und eigene Apps behalten ihre, und das Schließen des Fensters gibt die .NET-Laufzeit der Vorschau frei. Die Frame-Seite (`@coworkee/airworks/mudex-frame.html`) liegt auf dem Origin der App neben dem Bundle, die Komponente liest die Datei also mit dem Sitzungs-Cookie. Das Template hostet beides: `mudex-webcomponents` ist eine Dev-Abhängigkeit von `ClientApp`, der Vite-Build kopiert es und die Frame-Seite (als `coworkee-frame.html`) nach `wwwroot/mudex`, und der Host liefert die `.dat`-Dateien der .NET-Laufzeit aus. Anderswo übergeben Sie `mudExFrameUrl` an `useCoworkee`.

Der AirWorks-Desktop (Verknüpfungen, offene Fenster, Hintergrund) liegt je Browser im Local Storage, so wie AirWorks es vorsieht.

## Die Pakete

| Paket | Inhalt |
|---|---|
| `@coworkee/client` | Aus der OpenAPI-Beschreibung erzeugte Typen der Bibliotheks-API, `createCoworkeeClient()` (openapi-fetch mit Sitzungs-Cookie und CSRF-Header oder mit Bearer-Token), `apiRequest`, `ApiError` mit den Problem Details, `createRealtime()` (SignalR), `postNdjson` für gestreamte Antworten, BFF-Helfer, Theme, Texte und OData-Helfer. Ohne Framework, siehe [SDKs](sdk.md). |
| `@coworkee/airworks` | `useCoworkee`, die Apps oben, `crudApp`, Dialoge und Auswahlfelder, `renderEntityTable`, `renderWidget`, `mudExFrame`, die Styles. Braucht `@fgilde/airworks`. |

Die Quellen liegen in `typescript/` der Bibliothek. Das Template liefert beide als Tarballs mit, und eine erzeugte App nimmt sie aus `ClientApp/vendor` (`file:vendor/*.tgz`): `npm install` braucht dafür keine npm-Registry, eine App funktioniert also direkt nach einem Release und offline aus dem Template. AirWorks selbst kommt von GitHub mit einem festen Commit.

Jedes Release veröffentlicht sie zusätzlich auf npm in der Version des Releases: `a.b.c` bleibt, `a.b.c.d` wird zu `a.b.(c*1000+d)`, weil npm keinen vierten Teil kennt (Release `0.0.1.21` ist auf npm `0.0.1021`). Um sie von npm zu nehmen, erstellen Sie die App mit `coworkee new --airworks --airworks-packages npm` (Studio: *AirWorks-Pakete*, `dotnet new coworkee --airworksPackages npm`); `ClientApp/package.json` verweist dann genau auf die Version des Templates. Eine bestehende App stellen Sie um, indem Sie diese Version in `ClientApp/package.json` eintragen und `ClientApp/vendor` löschen.

`coworkee new --template-source <nupkg>` mit einem lokalen Build (`pwsh build/pack-local.ps1`) trägt den Ordner des Templates als Paketquelle `coworkee-local` in die `nuget.config` der App ein; so stellt die App die Coworkee-Pakete desselben Builds wieder her.
