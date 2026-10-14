# AirWorks-Client

Eine Coworkee-App kann neben der Blazor-App einen zweiten Client haben: einen Web-Desktop auf Basis von [AirWorks](https://fgilde.github.io/AirWorks/de.html), mit Fenstern, Taskleiste, mehreren Desktops, Ordnern, Suche und Pop-outs. Er meldet sich über denselben Auth-Server an (und damit über denselben Keycloak-Realm), spricht mit derselben API, zeigt Logo und Theme der App und bringt Apps für Einstellungen, Profil, Dateien, Daten und Benachrichtigungen mit. Die Bibliothek liefert die Apps als npm-Pakete; die App setzt sie zusammen und ergänzt eigene.

## Eine App damit erstellen

```bash
coworkee new Shop --airworks
```

In Coworkee Studio ist es die Karte *AirWorks-Client* unter *Allgemein*, mit einem Link zur AirWorks-Dokumentation. Die Solution bekommt das Projekt `Shop.AirWorks` und der AppHost eine Zeile:

```csharp
app.AddWeb<Projects.Shop_Web>();
app.AddAirWorks<Projects.Shop_AirWorks>();
```

Starten Sie den AppHost wie gewohnt und öffnen Sie *shop-airworks* aus dem Aspire-Dashboard. Zum Bauen braucht das Projekt Node.js 22 oder neuer: `dotnet build` führt `npm install` und `npm run build` in `ClientApp` aus (überspringen mit `-p:SkipClientBuild=true`).

## So meldet er sich an

`Shop.AirWorks` ist ein kleiner ASP.NET-Core-Host mit [Coworkee.Bff](../security/clients.md), genau wie der Web-Host der Blazor-App:

- `AddAirWorks` registriert ihn als zweiten Client am Auth-Server (`shop-airworks`, Redirect `/signin-oidc`, Scope `shop_api`) und richtet sein BFF auf die erste API aus.
- Die Anmeldung geht zum Auth-Server, der sein eigenes Formular und Keycloak anbietet, wie es der [Anmeldemodus](../security/authentication.md) vorgibt. Benutzer, Rollen und Berechtigungen sind die der Blazor-App.
- Die Tokens bleiben auf dem Server. Der Browser hält ein Sitzungs-Cookie namens `shop-airworks` (Browser senden Cookies an jeden Port von `localhost`, deshalb brauchen die beiden Clients verschiedene Namen) und schickt bei jeder Änderung `X-CSRF: 1` mit.
- `/api`, `/odata`, `/hubs`, `/openapi` und `/swagger` werden mit dem Access-Token an die API weitergeleitet.

## Die Client-App

`ClientApp/src/main.ts` gehört der App:

```ts
import { defaultApps, entityApp, useCoworkee } from '@coworkee/airworks';
import { AirWorks } from '@fgilde/airworks';
import '@fgilde/airworks/style.css';
import '@coworkee/airworks/style.css';
import logo from '../../../Shop.Web.Client/wwwroot/coworkee-icon.svg?url';

const coworkee = await useCoworkee(AirWorks, { title: 'Shop', logo });
await coworkee.register(
  ...defaultApps,
  entityApp({ entitySet: 'Products', icon: 'ratio-product.png', requiredPermissions: ['Catalog.Products.View'] }),
);
document.body.append(document.createElement('air-desktop'));
```

`useCoworkee` liest den Benutzer aus dem BFF, das Theme aus `/api/v1/themes/current` (Farben des Farbmodus des Benutzers als CSS-Variablen `--cw-primary`, `--cw-surface`, `--cw-text`, ..., die Primärfarbe als Systemfarbe von AirWorks, das Logo des Themes oder das übergebene), die Client-Einstellungen und die Texte der Sprache des Benutzers, meldet Anmeldung und Berechtigungen (`/api/v1/identity/permissions/me`) bei AirWorks an und konfiguriert es. Optionen von AirWorks selbst (Hintergründe, `initialShortcuts`, `initialApps`, ...) gehen an `airWorks: { ... }`.

| App | Id | Sichtbar, wenn |
|---|---|---|
| Profil: Name, Telefon, Kontoseiten, Abmelden | `coworkee.profile` | immer |
| Einstellungen: die des Benutzers, der Organisation und des Systems, aus den Einstellungsdefinitionen gebaut | `coworkee.settings` | immer; Organisation und System mit `Settings.Manage` |
| Dateien: Ordner, Hochladen (auch per Drag & Drop), Umbenennen, Löschen, Herunterladen | `coworkee.files` | `Files.View` |
| Dateivorschau mit der MudEx-Dateianzeige (PDF, Bilder, Office, Archive, Markdown, Audio, Video) | `coworkee.file-preview` | aus Dateien geöffnet |
| Daten: jedes OData-Entity-Set als blätterbare, sortierbare und durchsuchbare Tabelle | `coworkee.data` | die App hat OData |
| Benachrichtigungen: lesen, als gelesen markieren, löschen | `coworkee.notifications` | die App hat das Modul Notifications |
| API-Dokumentation (Swagger UI) | `coworkee.api-docs` | `ApiDocs.View` |
| `entityApp({ entitySet, ... })`: ein Entity-Set als eigene App | `coworkee.entity.{set}` | seine `requiredPermissions` |

Lassen Sie Apps weg, indem Sie nur die gewünschten registrieren, oder ersetzen Sie eine, indem Sie nach `defaultApps` eine eigene App mit derselben Id registrieren. Eigene Apps sind normale AirWorks-Apps (siehe [AirWorks-Dokumentation](https://fgilde.github.io/AirWorks/de.html)); eine Factory bekommt das `coworkee`-Objekt mit dem typisierten API-Client, `request`, der Übersetzungsfunktion `t`, dem Benutzer, dem Theme und `notify`:

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

Die Dateivorschau lädt die [Web Components von MudBlazor.Extensions](https://www.mudex.org) beim ersten Gebrauch von jsDelivr; übergeben Sie `mudExUrl` an `useCoworkee`, um das Bundle selbst zu hosten.

## Die Pakete

| Paket | Inhalt |
|---|---|
| `@coworkee/client` | Aus der OpenAPI-Beschreibung erzeugte Typen der Bibliotheks-API, `createCoworkeeClient()` (openapi-fetch mit Sitzungs-Cookie und CSRF-Header oder einem Bearer-Token), `apiRequest`, `ApiError` mit den Problem Details, BFF-Helfer, Theme-, Text- und OData-Helfer. Ohne Framework, siehe [SDKs](sdk.md). |
| `@coworkee/airworks` | `useCoworkee`, die Apps oben, `renderEntityTable`, `loadMudEx`, die Styles. Braucht `@fgilde/airworks`. |

Ihre Quellen liegen in `typescript/` der Bibliothek. Bis sie auf npm veröffentlicht sind, liefert das Template sie als Tarballs in `ClientApp/vendor` mit; AirWorks selbst kommt in einem festen Commit von GitHub.

## Grenzen

Chat, Berichte, der KI-Assistent und die Verwaltungsseiten (Benutzer, Rollen, Themes, Sprachen) bleiben in der Blazor-App. Der AirWorks-Desktop (Verknüpfungen, offene Fenster, Hintergrund) liegt im lokalen Speicher des Browsers, und der Client nutzt noch kein Realtime.
