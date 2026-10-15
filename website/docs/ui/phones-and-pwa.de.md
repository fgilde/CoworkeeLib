# Smartphones, installierbare App und Push

Die Blazor-Shell lässt sich auf dem Smartphone genauso bedienen wie am Schreibtisch, und die App installiert sich wie eine native. Push-Benachrichtigungen schalten Benutzer pro Gerät ein.

## Auf dem Smartphone

Unter 600 px Breite stellt sich das Layout von selbst um, du musst nichts einstellen:

- **App-Leiste**: Sprache, Theme, Rechts-nach-links, Farbmodus und Info wandern in ein gemeinsames *Mehr*-Menü. Das Benutzermenü zeigt nur noch den Avatar.
- **Navigation**: ein Overlay, das geschlossen startet und sich nach jedem Klick schließt. Die angeheftete Mini-Leiste vom Desktop gibt es nur auf breiteren Bildschirmen.
- **Datentabellen**: Zeilen werden zu Karten. Die leere Zone „Spalte hierher ziehen“ verschwindet (Ziehen braucht ohnehin eine Maus), das Suchfeld nimmt die volle Breite und Facetten-Menüs öffnen als Sheet von unten. Auf breiteren Bildschirmen bleibt die Spalte mit den Zeilenaktionen sichtbar, während eine breite Tabelle seitlich scrollt.
- **Bearbeitungsformulare und Seitenblätter** füllen den Bildschirm, kleine Dialoge behalten einen Rand. Formulare sind auf schmalen Bildschirmen schon einspaltig.
- **Seitenköpfe** setzen ihre Buttons unter den Titel.
- **Assistent**: Das Fenster deckt den ganzen Bildschirm ab.
- **Touch**: Auf Touchscreens sind Icon-Buttons, Menüeinträge und Navigationslinks mindestens 44 px hoch.
- **Smartphones mit Notch**: App-Leiste, Inhalt, Navigation und Sheets halten Abstand zu den Safe Areas. Das `App.razor` der Vorlage hat `viewport-fit=cover` im Viewport-Tag; ältere Apps ergänzen es:

```html
<meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover" />
```

Die Anmeldeseiten des Auth-Servers machen es genauso: Auf dem Smartphone füllt das Formular den Bildschirm, Eingabefelder nutzen 16 px Schrift, damit iOS nicht hineinzoomt, und Links sowie das Kästchen *Angemeldet bleiben* sind gut zu treffen.

Deine eigenen Seiten bekommen die Regeln ebenfalls, solange sie `PageHeader` und `CoworkeeDataTable` nutzen. Für eigene Komponenten verrät dir `IBrowserViewportService` von MudBlazor den Breakpoint.

## Installierbare App

Eine Zeile im Head von `MyApp.Web/App.razor` macht die App zur Progressive Web App:

```razor title="MyApp.Web/App.razor"
<link rel="icon" href="coworkee-icon.svg" />
<Coworkee.Bff.Pwa.CoworkeePwa />
```

Die Komponente verlinkt das Web-Manifest und färbt die Titelleiste des Browsers wie die App-Leiste im Theme des Besuchers. Den Rest liefert `Coworkee.Bff`:

| Adresse | Was es ist |
|---|---|
| `/manifest.webmanifest` | Name der App, Farben und Logo aus dem Theme der Organisation (das Logo des Themes, sonst das Icon der App). |
| `/service-worker.js` | Der Service Worker; seine Version folgt den statischen Dateien der App, jedes Deployment bringt also einen neuen. |
| `/bff/pwa/icon` | Das Icon: das Logo des Themes oder das eingestellte Icon. |
| `/bff/pwa/offline` | Die Seite ohne Netz, im Theme und in der Sprache des Benutzers. |

Name und Icon kommen aus der Konfiguration; Aspire setzt beides aus `DisplayName` und `LogoUrl`:

```json title="appsettings.json der Web-App"
"Coworkee": { "Bff": { "Pwa": { "Name": "Mein Shop", "Icon": "/coworkee-icon.svg" } } }
```

Was der Service Worker tut:

- Er speichert eine einzige statische Datei, die Offline-Seite, und zeigt sie, wenn eine Seite ohne Netz nicht lädt. Ihre Framework-Dateien hält Blazor in einem eigenen Cache.
- Alle anderen Anfragen gehen unverändert ans Netz: Anmeldung, `/bff`, `/api`, `/odata`, `/hubs`, Dateien und alles außer GET. Sitzungs-Cookie, `X-CSRF`-Header und Antiforgery funktionieren wie bisher, und keine Antwort der API landet in einem Cache.
- Eine neue Version wartet, bis der Benutzer zustimmt: Die App zeigt *Eine neue Version der App ist verfügbar* mit einem Button *Neu laden*.
- Bietet der Browser die Installation an, zeigt die App einmal pro Browser einen Hinweis, und das Benutzermenü behält den Eintrag *App installieren*.

Die App registriert den Worker nur auf Seiten, die ein Manifest verlinken; ohne die Komponente ändert sich also nichts. Die Vorlage hat dafür die Option `pwa` (standardmäßig an, `coworkee new --pwa false` lässt sie weg).

## Push-Benachrichtigungen

Mit dem Notifications-Modul erreicht jede Benachrichtigung das Smartphone oder den Desktop des Benutzers auch als Push-Nachricht, selbst wenn die App geschlossen ist. Ein Klick auf die Nachricht öffnet den Link der Benachrichtigung in der App.

In der Entwicklung reicht eine Zeile im App-Host. Sie erzeugt beim ersten Start ein VAPID-Schlüsselpaar und legt es in den User Secrets des App-Hosts ab (`Parameters:{name}-push-public-key` und `-push-private-key`):

```csharp title="MyApp.AppHost/AppHost.cs"
var app = builder.AddCoworkeeApp("myapp", options =>
{
    options.UsePush();
});
```

Die Vorlage fügt sie ein, wenn `pwa` und `notifications` an sind. Ohne Aspire oder in Produktion bekommt jeder Dienst mit dem Notifications-Modul dasselbe Paar:

```json
"Coworkee": {
  "Notifications": {
    "Push": { "PublicKey": "<base64url>", "PrivateKey": "<base64url>", "Subject": "mailto:ops@example.com" }
  }
}
```

Der öffentliche Schlüssel ist der unkomprimierte P-256-Punkt, der private der 32-Byte-Skalar, beide base64url. `npx web-push generate-vapid-keys` gibt ein Paar in genau diesem Format aus. Behalte das Paar: Ein neues bringt jedes abonnierte Gerät zum Schweigen, bis sein Benutzer Push wieder einschaltet. Ohne `Subject` bekommen die Push-Dienste `PublicAppUrl` als Kontakt. Auf der Seite *App-Einstellungen* ist das Paar ausgeblendet.

Benutzer schalten Push pro Gerät auf der Benachrichtigungsseite ein und aus (auch im Tab *Benachrichtigungen* der Kontoseite). Die Karte listet ihre Geräte und entfernt alte. Ohne Schlüssel bleibt die Karte unsichtbar.

Die API dahinter:

| Endpunkt | |
|---|---|
| `GET /api/v1/notifications/push` | Öffentlicher Schlüssel (null ohne Schlüssel) und die Geräte des Benutzers. |
| `PUT /api/v1/notifications/push` | Speichert das Abonnement des Browsers (`endpoint`, `p256dh`, `auth`, `device`). Meldet sich im selben Browser ein anderer Benutzer an, wandert es zu ihm. |
| `DELETE /api/v1/notifications/push/{id}` | Entfernt ein Gerät des Benutzers. |

Als Endpunkt gelten nur `https`-Adressen von Push-Diensten im Internet: keine IP-Adressen, keine Ports, keine lokalen Hostnamen.

Die Zustellung läuft als Hintergrundjob, nachdem die Benachrichtigung gespeichert ist. `INotifier` löst `NotificationCreated` über die Outbox aus, `PushDeliveryJob` schickt Titel und Text in der Sprache des Benutzers mit dem Link der Benachrichtigung. Geräte, die der Push-Dienst nicht mehr kennt (404 oder 410), werden entfernt. Die Bibliothek verschlüsselt die Nachricht (RFC 8291) und signiert das VAPID-Token (RFC 8292) mit der Kryptografie von .NET, ganz ohne Zusatzpaket. Willst du über einen eigenen Dienst senden, registrierst du deinen `IPushSender`:

```csharp
builder.Services.AddSingleton<IPushSender, MyPushSender>();
```

!!! note "iPhone und iPad"
    Safari stellt Push ab iOS 16.4 nur einer App zu, die zum Home-Bildschirm hinzugefügt wurde (*Teilen*, dann *Zum Home-Bildschirm*).
