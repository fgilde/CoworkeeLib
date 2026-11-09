# Audit

Das Audit protokolliert jede Änderung an den Entities der App Feld für Feld: wer (und wer als er angemeldet war), wann, alter Wert, neuer Wert. Es wird in derselben Transaktion wie die Änderung geschrieben (Tabelle `cw.AuditEntries`) und lügt deshalb nie.

`Coworkee.Auditing` (`CoworkeeAuditingModule`) bringt das Audit-Log, seine Endpunkte, den Verlauf eines Datensatzes und die Seiten des [Sicherheitsprotokolls](#sicherheitsprotokoll) und des [Request-Logs](#request-log); die Vorlage hat es (Option `auditing`, standardmäßig an). Das Protokoll selbst schreibt der DbContext der App.

## Audit-Protokoll

- Jede angelegte, geänderte und gelöschte Entity wird protokolliert, außer Owned Types, [anonyme Datensätze](#anonyme-datensatze) und was ausgenommen ist: `[NotAudited]` an einem Typ oder Feld (oder `IsNotAudited()` in einem Model-Contributor) lässt es weg, `[Sensitive]` (oder `IsSensitive()`) protokolliert, dass sich ein Feld geändert hat, ohne den Wert (`***`).
- Die Seite **Audit-Log** (*Administration > Überwachung > Audit-Log*, `/cw/admin/audit`) listet die Änderungen der eigenen Organisation (die System-Organisation sieht auch Einträge ohne Organisation) als Datentabelle mit Suche, Zeitraum, *Nur meine Änderungen* und wer als der Akteur angemeldet war (*angemeldet durch*).
- `<AuditTimeline EntityType="Brand" EntityId="@brand.Id.ToString()" />` zeigt die Historie einer Entity auf ihrer eigenen Seite.
- Entities mit `IVersioned` behalten zusätzlich vollständige Schnappschüsse (`cw.EntitySnapshots`, eine `Revision` pro Speichern). Ein Modul oder eine App registriert einen Typ mit einem `IVersionedTypeContributor` (`context.Add<Theme>("Theme", permission)`); `<VersionHistory Type="Theme" Id="..." />` listet dann die Revisionen und stellt für Inhaber dieser Berechtigung eine ältere wieder her.

| Endpunkt | |
|---|---|
| `GET /api/v1/audit?entityType=&entityId=&actorId=&impersonatorId=&from=&to=&page=&pageSize=` | die Einträge (`Audit.View`); auch das OData-Set `AuditEntries` |
| `GET /api/v1/versions/{type}/{id}`, `GET .../{revision}`, `POST .../{revision}/restore` | Schnappschüsse eines `IVersioned`-Typs (mit seiner registrierten Berechtigung) |

Die Berechtigung `Audit.View` zeigt das Audit-Log und den Verlauf. Mit dem Modul [Aufbewahrung](retention.md) löschen die Ziele `audit` und `snapshots` alte Einträge; der Export personenbezogener Daten hat einen Abschnitt `activity` (was der Benutzer geändert hat: Typ, Id, Aktion, Zeit), und das Löschen eines Benutzers behält das Protokoll ohne ihn: Seine ID wird zum Platzhalter für gelöschte Benutzer, die Werte der mit ihm gelöschten Datensätze werden `***`, seine ID und Adresse verschwinden aus den übrigen Werten ([Regeln](../security/privacy.md#der-audit-verlauf)).

## Verlauf eines Datensatzes und Wiederherstellen

Das Audit kennt schon jede Feldänderung, also kann es auch den Verlauf eines Datensatzes zeigen und ihn zurückholen. Du schaltest das pro Entity-Set ein. Wiederherstellen läuft dann über deinen eigenen Update-Command, mit dessen Berechtigungen, Validierung und Audit.

```csharp
context.Services.AddODataEntity<Product>("Products", CatalogPermissions.Products.View);
context.Services.AddEntityHistory("Products", CatalogPermissions.Products.Edit, (Product p) => new AddEditProductCommand(p.Id, new AddEditProductRequest
{
    Name = p.Name, Barcode = p.Barcode, Description = p.Description, ImageDataUrl = p.ImageDataUrl, Rate = p.Rate, BrandId = p.BrandId,
}));
```

Das zweite Argument ist die Berechtigung zum Wiederherstellen. Das Lambda macht aus einem Datensatz den Update-Command. Beim Wiederherstellen bekommt der Datensatz die früheren Werte und geht so in den Command; Felder, die der Command nicht kennt, bleiben, wie sie sind.

Im Client schaltest du entweder die Zeilenaktion einer Tabelle ein oder setzt die Zeitleiste auf eine Detailseite:

```razor
<CoworkeeDataTable T="ProductDto" EntitySet="Products" History="true" ... />

<CoworkeeEntityHistory EntitySet="Products" Id="product.Id" OnRestored="ReloadAsync" />
```

- Die Zeitleiste zeigt, wer wann was geändert hat, mit Feldnamen (`BrandId` wird zu *Brand*), Enum-Namen und dem Namen eines verknüpften Datensatzes. Lange Texte zeigen einen Wort-Diff.
- **Diesen Stand wiederherstellen** setzt den Datensatz auf den Stand direkt nach diesem Eintrag zurück; der Rückgängig-Knopf neben einem Feld macht nur diese eine Änderung rückgängig. Beides zeigt vorher, welche Werte herauskommen, und fragt nach.
- Datensätze mit `ISoftDelete` kommen auch zurück: Der Knopf **Gelöschte Datensätze** der Tabelle listet sie, und beim Wiederherstellen wird der Datensatz zuerst wieder aktiv.
- Für den Verlauf brauchst du `Audit.View` und die Leseberechtigung des Entity-Sets, fürs Wiederherstellen die registrierte Berechtigung. Alles bleibt im Mandanten des Benutzers.
- `[Sensitive]`-Felder erscheinen als `***` und werden nie zurückgeschrieben; Felder, die das OData-Set verbirgt, tauchen im Verlauf nicht auf.

Das Audit-Log (`/cw/admin/audit`) bietet dieselben Aktionen pro Eintrag, wenn dessen Typ zu so einem Set gehört: Öffne eine Zeile und wähle **Diese Änderung rückgängig machen** (nur die Felder dieses Eintrags gehen zurück, spätere Änderungen an anderen Feldern bleiben), **Stand vor dieser Änderung wiederherstellen** (dieser Eintrag und alles danach wird zurückgerollt), **Datensatz wiederherstellen** beim Löschen eines Soft-Delete-Datensatzes oder **Verlauf des Datensatzes öffnen**. Alle zeigen dieselbe Vorschau und fragen nach. Hat ein späterer Eintrag ein Feld geändert, das ein Rückgängigmachen zurücksetzt, markiert die Vorschau das Feld mit *Später geändert* und warnt dich, dass der neuere Wert ersetzt wird. Ohne die Berechtigung zum Wiederherstellen siehst du nur den Verlauf; Einträge anderer Typen zeigen nichts.

Die Endpunkte liegen unter `/api/v1/history`: `GET /` (die Sets, die du lesen darfst, mit `CanRestore`) und pro Set `/{entitySet}`: `GET /{id}`, `POST /{id}/preview`, `POST /{id}/restore` und `GET /deleted`. Vorschau und Wiederherstellen nehmen `{ entryId, property?, mode, version? }` mit `mode` `After` (Standard, der Stand direkt nach dem Eintrag), `Before` oder `Undo`. Die Vorschau liefert eine `version` des Datensatzes; das Wiederherstellen schickt sie zurück und antwortet `409`, wenn sich der Datensatz dazwischen geändert hat (ein Wert, das Löschen oder ein neuer Verlaufseintrag). Die Blazor-Dialoge und das AirWorks-Datensatzfenster zeigen dann die neue Vorschau.

Hart gelöschte Datensätze gibt es in der Tabelle nicht mehr, also gibt es auch nichts zu aktualisieren. Nimm `ISoftDelete` für alles, was jemand vielleicht zurückhaben möchte.

## Sicherheitsprotokoll

Das Sicherheitsprotokoll hält fest, was mit Konten passiert: Anmeldungen (erfolgreich oder fehlgeschlagen, mit Grund), Abmeldungen, Sperren, Änderungen an Passwörtern, E-Mail-Adressen, Zwei-Faktor-Anmeldung und Passkeys, *Anmelden als* (Beginn und Ende), beendete Sitzungen (mit Grund und wer sie beendet hat), API-Schlüssel (erstellt, widerrufen, die erste Nutzung eines Tages) und die Rollen und eigenen Berechtigungen, die ein Benutzer bekommt oder verliert. Es ist immer an und wird von jedem Host mit der App-Datenbank geschrieben (Auth-Server und API), in die Tabelle `cw.SecurityEvents`.

Jeder Eintrag hat die Zeit, die Organisation, den **Benutzer**, um dessen Konto es geht, den **Handelnden**, wenn es jemand anderes war (ein Administrator, der den Benutzer sperrt), den **Angemeldeten**, wenn jemand als handelnder Benutzer angemeldet war, Aktion und Ergebnis mit Grund (`InvalidCredentials`, `UnknownUser`, `LockedOut`, `NotAllowed`, `FailedAttempts`, `Administrator`, `SessionLimit`, ...), den Client, die IP-Adresse, den Browser (User Agent), die Trace-ID und Details als JSON (die eingegebene Anmeldung bei einem unbekannten Konto, die Anmeldemethode, die Rolle, die Berechtigung, der Schlüssel).

- Änderungen an Zugangsdaten und Zugriffen kommen aus den Speichervorgängen selbst (ein EF-Core-Interceptor von Coworkee.Identity), daher zählt jeder Weg dorthin: eigenes Profil, Administration, Reset-Links, Fehlversuche. Sie werden in derselben Transaktion wie die Änderung gespeichert.
- Anmeldungen, Abmeldungen und *Anmelden als* schreiben die Anmeldeseiten und der End-Session-Endpunkt des Auth-Servers; API-Schlüssel die Schlüssel-Authentifizierung. Eine Anmeldung, die das [Sitzungslimit](../security/clients.md#sitzungslimit) ablehnt, ist eine fehlgeschlagene Anmeldung mit dem Grund `SessionLimit`.
- Jede beendete Sitzung ist ein Eintrag `SessionEnded`, geschrieben aus der Outbox (`UserSessionEnded`): Der Grund ist der Grund des Endes (`ended`, `ended-by-user`, `limit`, `logged-out`, `replaced` oder warum sich der Security-Stamp geändert hat), der Handelnde der Administrator oder Benutzer, der sie beendet hat, die Details enthalten `sessionId` und `endedBy`.
- Dein Code schreibt eigene Einträge über `ISecurityLog` (Coworkee.Application.Auditing), mit einer eigenen Aktion, zum Beispiel, wenn jemand einen vertraulichen Bericht herunterlädt:

```csharp
await securityLog.WriteAsync(new SecurityLogEntry("ReportDownloaded", userId, Details: new { reportId }));
```

Benutzer, Organisation und Angemeldeter sind standardmäßig die aktuellen (die Organisation wird über den Benutzer nachgeschlagen, wenn der Request keine hat); Adresse, Browser, Client und Trace kommen aus dem Request. Der Eintrag wird sofort in einem eigenen Scope gespeichert, er bleibt also, auch wenn die Unit of Work des Aufrufers es nicht tut. Halte Geheimnisse aus den Details heraus.

Die Seite **Sicherheitsprotokoll** (*Administration > Überwachung*, `/cw/admin/security-log`, Berechtigung `Audit.SecurityLog`) listet die Einträge der eigenen Organisation (die Systemorganisation sieht auch Einträge ohne, etwa Fehlanmeldungen unbekannter Konten) mit Suche, Zeitraum, Facetten für Aktion, Ergebnis und Grund sowie Details, Browser und Trace einer Zeile. Die Benutzerseite verlinkt auf die Einträge des Benutzers (`/cw/admin/security-log?user={id}`). Die Liste ist das OData-Set `SecurityEvents`.

## Request-Log

Das Request-Log hält jeden API-Aufruf des API-Hosts fest: Zeit, Organisation, Benutzer (und wer als er angemeldet war), Client oder API-Schlüssel, Methode, Pfad und Query-String, Routenvorlage, den Request, den der Endpunkt geschickt hat (`IDispatcher`), Status, Dauer, IP-Adresse, Browser, Trace-ID, eine Zusammenfassung einer unbehandelten Exception, die Header von Anfrage und Antwort und die Inhalte von Anfrage und Antwort. Tabelle `cw.RequestLog`, geschrieben vom `CoworkeeAuditingModule` in dem Host, der es hat (die API; das Backend for Frontend der Web-App leitet an sie weiter, seine Aufrufe stehen also einmal im Log).

Was es behält und verbirgt:

- **Header**: alle, wobei die Werte von `Authorization`, `Cookie`, `Set-Cookie`, `X-Api-Key` und jedem Namen, der *password*, *secret*, *token*, *apikey* oder *credential* enthält, durch `***` ersetzt werden. Dieselben Namen werden im Query-String und in Inhalten verborgen, in jeder Tiefe (`clientSecret`, `refresh_token`, `nested.apiKey`); ein Objekt oder eine Liste unter einem solchen Namen wird als Ganzes `***`.
- **Inhalte**: nur JSON (`application/json`, `*+json`, `text/json`) und Formulare (`application/x-www-form-urlencoded`, als JSON-Objekt gespeichert), jeweils bis zur eingestellten Größe (standardmäßig 32 KB) und danach gekürzt (markiert mit `…`; ein gekürzter Inhalt ist bis zum Schnitt bereinigt). Dateien, Multipart, NDJSON, Server-Sent Events und Binärdaten werden nie behalten; gestreamte Antworten streamen weiter.
- **Ausgelassen**: `OPTIONS`-Requests, Health und Telemetrie (`/health`, `/alive`, `/metrics`), API-Doku (`/openapi`, `/swagger`), die Realtime-Verbindung (`/hubs`), statische Dateien und Endpunkte mit `.SkipRequestLog()` (oder `[SkipRequestLog]`, Coworkee.AspNetCore). Unbekannte Adressen unter `/api` werden protokolliert (Scanner, kaputte Clients).
- **Adresse**: die Adresse der Verbindung. Hinter dem Backend for Frontend oder einem Proxy ist das der Proxy, außer der Host wendet Forwarded Headers an (`UseForwardedHeaders`); `X-Forwarded-For` steht so oder so in den Headern.

Einstellungen (*Einstellungen > Request-Log*, Global; sie gelten sofort auf jeder Instanz):

| Einstellung | Standard | |
|---|---|---|
| `RequestLog.Requests` (*Aufrufe protokollieren*) | Lesend, Schreibend | welche Aufrufe mit Headern und Inhalten protokolliert werden: *Lesend* (GET, HEAD, OData-Abfragen), *Schreibend* (POST, PUT, PATCH, DELETE); nichts gewählt schaltet das Request-Log aus |
| `RequestLog.MaxBodyKb` (*Inhaltsgröße (KB)*) | 32 | Kilobyte, die von jedem Inhalt bleiben; 0 behält keine Inhalte |
| `RequestLog.RedactedNames` (*Verborgene Namen*) | | weitere Header- und Feldnamen zum Verbergen, kommagetrennt |

**Performance.** Die Middleware kopiert nur, was vorbeikommt: die ersten Kilobyte jedes Inhalts, während der Endpunkt ihn liest oder schreibt, nie den ganzen Inhalt. Der Aufruf kommt in eine begrenzte Warteschlange (2000 Aufrufe), die ein Hintergrunddienst in Paketen von 200 mit je einem EF-Core-Speichern sichert; das Bereinigen passiert dort, abseits des Request-Pfads. Eine volle Warteschlange verwirft den Aufruf, statt die App zu bremsen, und zählt ihn in der Metrik `coworkee.request_log.dropped` des Meters `Coworkee.Auditing` (das Template trägt den Meter in OpenTelemetry ein). Gemessen in den Tests der Bibliothek (Host im Prozess, je 2 KB JSON hin und zurück): etwa 0,1 ms mehr pro Aufruf mit beiden Inhalten.

Die Seite **Request-Log** (*Administration > Überwachung*, `/cw/admin/request-log`, Berechtigung `Audit.Requests`) listet die Aufrufe der eigenen Organisation (die Systemorganisation sieht auch anonyme) mit Suche, Zeitraum und Facetten für Methode, Route und Status; die Liste lädt nie Header oder Inhalte. Eine geöffnete Zeile lädt sie; die Inhalte brauchen `Audit.Requests.Bodies` (schließt `Audit.Requests` ein), weil sie personenbezogene Daten enthalten können. **Trace öffnen** springt zum Trace in Grafana (Tempo) oder im Aspire-Dashboard, wenn der App-Host sie betreibt (`Coworkee:Services:grafana` oder `dashboard`).

| Endpunkt | |
|---|---|
| OData `SecurityEvents` | das Sicherheitsprotokoll (`Audit.SecurityLog`) |
| OData `RequestLog` | das Request-Log ohne Header und Inhalte (`Audit.Requests`) |
| `GET /api/v1/audit/requests/{id}` | ein Aufruf mit Headern, und Inhalten für `Audit.Requests.Bodies` (`RequestLogDetailDto`, sonst `BodiesHidden`) |
| `GET /api/v1/audit/trace-viewer` | `{ urlTemplate }` mit `{traceId}`, oder null ohne Grafana und Dashboard (eine der beiden Log-Berechtigungen) |

Der Blazor-Client hat `IAuditApi` für die letzten beiden. Mit dem Modul [Retention](retention.md) löschen die Ziele `security-events` (365 Tage) und `request-log` (30 Tage) alte Einträge. Der Export personenbezogener Daten hat die Abschnitte `securityLog` und `requests`; das Löschen eines Benutzers behält die Einträge ohne ihn ([Regeln](../security/privacy.md)).

## Anonyme Datensätze

Manche Zeilen dürfen nicht auf die Person zurückführbar sein, die sie geschrieben hat: Antworten einer anonymen Umfrage, Hinweisgebermeldungen, Abstimmungen. Markiere den Typ mit `IAnonymous` (oder leite von `AnonymousEntity` ab):

```csharp
public sealed class SurveyResponse : AnonymousEntity, IMultiTenant   // zufällige ID mit Guid.NewGuid()
{
    public Guid SurveyId { get; set; }
    public DateTimeOffset SubmittedAt { get; set; }
    public string? AnswersJson { get; set; }
    public Guid TenantId { get; set; }
}

response.SubmittedAt = response.Truncate(clock.GetUtcNow());   // eigene Zeitstempel genauso kürzen
```

Für solche Zeilen

- schreibt die Bibliothek kein `CreatedBy`, `ModifiedBy` oder `DeletedBy` und kürzt `CreatedAt`, `ModifiedAt` und `DeletedAt` auf den UTC-Tag;
- schreibt sie **keinen Audit-Eintrag** (also auch keinen Verlauf) und `IVersioned`-Snapshots ohne `CreatedBy` und mit gekürzter Zeit;
- verschickt sie Domain Events (Outbox), Webhooks und Elsa-Entity-Trigger **ohne Benutzer und Trace-ID**, ihre Handler laufen also ohne Benutzer; Realtime-Änderungsereignisse tragen nie einen Benutzer;
- lässt sie IDs, wie sie sind: IDs von `Entity` sind zeitlich sortiert (`Guid.CreateVersion7()`, auf die Millisekunde), `AnonymousEntity` setzt `Guid.NewGuid()`. Ein Typ mit anderer Basisklasse setzt `Id = Guid.NewGuid()` in seinem Konstruktor.

`TimeSpan IAnonymous.TimestampPrecision => TimeSpan.FromHours(1);` ändert die Genauigkeit (`TimeSpan.Zero` behält exakte Zeiten); eine Klasse, die von `AnonymousEntity` erbt, führt dafür `IAnonymous` noch einmal auf.

Was deine Aufgabe bleibt:

- Keine Person an der Zeile speichern (`EmployeeId`) oder über eine Navigation erreichbar machen, und Kind-Zeilen (`SurveyAnswer`) ebenfalls mit `IAnonymous` markieren.
- Kleine Gruppen verraten Personen: Ergebnisse erst ab einer Mindestzahl an Antworten zeigen.
- Workflows, Kommentare, Reaktionen, Dateien und Benachrichtigungen speichern ihren eigenen Akteur; hänge sie nicht an anonyme Zeilen. Integration Events, die du selbst veröffentlichst (`IEventBus`), tragen den aktuellen Benutzer: veröffentliche sie in `CurrentUserScope.Begin(new ImpersonatedUser(null, tenantId))`, wenn sie zu einer anonymen Zeile gehören.
- Request-Logs und Traces (OpenTelemetry) zeigen weiterhin, wer den Endpunkt wann aufgerufen hat; halte ihre Aufbewahrung kurz oder nimm den Endpunkt aus dem detaillierten Logging.
