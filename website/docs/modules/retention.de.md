# Aufbewahrung

`Coworkee.Retention` legt fest, wie lange Daten aufbewahrt werden. Module melden die Datenarten, die ohne Ende wachsen (Audit-Trail, Outbox, Protokolle, gelöschte Datensätze, Dateien, Tokens), ein nächtlicher Job löscht, was älter ist als die eingestellten Tage, und die Admin-Seite *Aufbewahrung* (`/admin/retention`) stellt die Tage ein und löscht auf Wunsch sofort.

```csharp
[DependsOn(typeof(CoworkeeRetentionModule))]
public sealed class MyAppDatabaseModule : CoworkeeModule;
```

Das Modul bringt die Tabelle `cw.RetentionRuns` (letzter Lauf pro Datenart) mit: **Migration anlegen**, wenn du es in eine bestehende App aufnimmst.

## Datenarten

Jede Datenart wird über alle Organisationen hinweg gelöscht. Die Bibliothek löscht nach einem Update nie von selbst: Alles, was bisher für immer blieb, bleibt für immer, bis jemand Tage einstellt. Nur das Zustellprotokoll der Webhooks behält seine 30 Tage, und Anhänge endgültig gelöschter Datensätze gehen nach einem Tag (sie nützen niemandem mehr).

| Schlüssel | Modul | Daten | Alter nach | Standard |
|---|---|---|---|---|
| `audit` | Retention | Audit-Trail (`cw.AuditEntries`) | `OccurredAt` | nie |
| `snapshots` | Retention | ältere Revisionen versionierter Entities (`cw.EntitySnapshots`), die neueste jeder Entity bleibt | `CreatedAt` | nie |
| `outbox` | Retention | verarbeitete Outbox-Nachrichten, ausstehende bleiben | `ProcessedAt` | nie |
| `access-tokens` | Retention | abgelaufene anonyme Tokens: temporäre Dateilinks, Export-Downloads | `ExpiresAt` | 1 Tag |
| `soft-deleted` | Retention | jeder weich gelöschte Typ des Modells außer Dateien | `DeletedAt` | nie |
| `inbox` | EventBus | verarbeitete Integrations-Events (`cw.InboxMessages`) | `ProcessedAt` | nie |
| `ai-tool-calls` | Ai | Audit der Werkzeugaufrufe | `At` | nie |
| `ai-sessions` | Ai | Assistenten-Unterhaltungen mit ihren Nachrichten | `UpdatedAt` | nie |
| `webhook-deliveries` | Webhooks | Zustellprotokoll ohne ausstehende Zustellungen | `CreatedAt` | `LogRetentionDays` (30) |
| `mail-log` | Mailing | gesendete, fehlgeschlagene und übersprungene Mails | `QueuedAt` | nie |
| `notifications` | Notifications | In-App-Benachrichtigungen | `CreatedAt` | nie |
| `data-transfer-jobs` | OData | beendete Importe und Exporte mit ihren Dateien | `FinishedAt` | nie |
| `orphaned-attachments` | Files | [Anhänge an Datensätzen](attachments.md), deren Datensatz endgültig weg ist, mit ihren Blobs | `CreatedAt` | 1 Tag |
| `deleted-files` | Files | weich gelöschte Dateien mit ihren Blobs, danach leere weich gelöschte Ordner | `DeletedAt` | nie |
| `backups` | Backup | Datenbank-Backups mit ihrer ZIP-Datei, das neueste Backup bleibt immer | `CreatedAt` | nie |
| `auth-tokens` | AuthServer | was der Prune von OpenIddict entfernt: Tokens, die weder gültig noch inaktiv sind, zu einer ungültigen Autorisierung gehören oder abgelaufen sind; Autorisierungen ohne Tokens, die ungültig oder ad hoc sind | Erstellung | nie |

- `audit`: Entity-Historie und Wiederherstellen lesen den Audit-Trail. Ältere Änderungen lassen sich dann nicht mehr anzeigen oder rückgängig machen.
- `snapshots`: Versionshistorie und Wiederherstellen versionierter Entities (etwa Mail-Vorlagen) lesen sie; ältere Versionen lassen sich dann nicht mehr anzeigen oder wiederherstellen.
- `soft-deleted`: jeder konkrete weich gelöschte Typ des Modells, einer nach dem anderen, auch unter einer Wurzel, die nicht weich gelöscht wird; ein Typ in einer Table-per-Hierarchy löscht genau seine eigenen Zeilen. Hält ein Fremdschlüssel einen Typ noch fest (ein weich gelöschter Besitzer, dessen Kinder bleiben) oder kann EF ihn nicht in Masse löschen (Table per Type), meldet der Lauf das als Fehler und nimmt sich den nächsten Typ vor. Typen, die mit `AddRetentionOwnedType<T>()` angemeldet sind, und ihre abgeleiteten Typen bleiben außen vor.
- `deleted-files`: Ein Blob, der sich nicht löschen lässt, hält den Lauf nicht auf; er wird geloggt und im Fehler des Laufs genannt (die Zeile ist dann schon weg).
- `auth-tokens`: Der Prune von OpenIddict löscht 1000 Zeilen pro Anweisung. Die Zählung lässt Autorisierungen aus, die erst im selben Prune ihre letzten Tokens verlieren.
- `data-transfer-jobs`: Ein Import, dessen Job weg ist, lässt sich nicht mehr rückgängig machen.

Neue Apps aus dem Template starten mit technischen Standards in `src/MyApp.Api/appsettings.json`: `outbox`, `inbox` und `auth-tokens` 14 Tage, `data-transfer-jobs` 30 Tage.

## Einstellungen

Jede Datenart hat die Global-Einstellung `Retention.{key}.Days` (leer oder `0`: alles behalten). Die Einstellungen stehen auf der Admin-Seite *Aufbewahrung*, nicht in den Einstellungs-Tabs. Der Standard kommt vom Modul oder aus der Konfiguration:

```json
{
  "Coworkee": {
    "Settings": {
      "Defaults": {
        "Retention.audit.Days": "365",
        "Retention.mail-log.Days": "90"
      }
    }
  }
}
```

Das Webhook-Modul liest `Coworkee:Webhooks:LogRetentionDays` weiterhin als Standard von `Retention.webhook-deliveries.Days`. Hosts ohne das Retention-Modul behalten die bisherige tägliche Bereinigung des Webhook-Protokolls; mit dem Modul tut sie nichts, und der nächtliche Retention-Job löscht.

## Die Admin-Seite

*Monitoring → Aufbewahrung*, für Benutzer der Systemorganisation mit `Retention.Manage` (Administratoren bekommen sie wie jede andere Permission). Eine Zeile pro Datenart:

- **Behalten (Tage)**: leer behält alles; schon beim Tippen zählt *Betroffen jetzt*, was diese Tage löschen würden. *Speichern* speichert alle geänderten Zeilen.
- **Letzter Lauf**: wann, wie viele Zeilen und der Fehler des letzten Laufs. Eine laufende Zeile zeigt einen Fortschrittsbalken; die Seite fragt alle 2 Sekunden den Laufzustand ab (`?counts=false`, ohne Zählung) und zählt neu, sobald der Lauf fertig ist.
- **Jetzt löschen** arbeitet nur mit den gespeicherten Tagen: Solange eine Zeile ungespeicherte Änderungen hat, ist der Button aus (*Erst speichern*). Er fragt mit der Anzahl nach (*12.480 Einträge älter als 365 Tage löschen?*) und läuft als Hintergrundjob. Das Log und die Tabelle merken sich, wer ihn gestartet hat.

Der Abschnitt *Aufbewahrung* der Systemstatus-Seite (`/admin/system-status`) zeigt den letzten Lauf und die Fehler jeder Datenart.

| Endpoint | |
|---|---|
| `GET /api/v1/retention` | Datenarten mit Tagen, Standard (nach der Konfiguration), betroffenen Zeilen, letztem Lauf; `?counts=false` lässt die Zählung weg |
| `GET /api/v1/retention/{key}/count?days=N` | Zeilen älter als `N` Tage |
| `PUT /api/v1/retention` | `{ "audit": 365, "mail-log": null }` speichert die Tage |
| `POST /api/v1/retention/{key}/run` | Body `{ "days": N }` (mindestens 1): `202` und ein Hintergrundjob, `409`, solange der Schlüssel läuft |

Alle brauchen `Retention.Manage` und die Systemorganisation (sonst `403` mit `retention.system_only`).

## Eigene Datenart

`IRetentionTarget` implementieren und registrieren; die Datenart erscheint auf der Seite und im nächtlichen Lauf.

```csharp
internal sealed class SensorReadingRetention(CoworkeeDbContext db) : IRetentionTarget
{
    public string Key => "sensor-readings";

    public string Title => "Sensor readings";   // englischer Text, zugleich Lokalisierungs-Key

    public string? Description => "Raw readings of the devices; the daily sums stay.";

    public int? DefaultDays => null;            // null: alles behalten, bis jemand Tage einstellt

    public Task<long> CountAsync(DateTimeOffset before, CancellationToken cancellationToken) =>
        Older(before).LongCountAsync(cancellationToken);

    public Task<long> DeleteAsync(DateTimeOffset before, CancellationToken cancellationToken) =>
        Older(before).OrderBy(r => r.At).ExecuteDeleteInBatchesAsync(cancellationToken);

    private IQueryable<SensorReading> Older(DateTimeOffset before) =>
        db.Set<SensorReading>().IgnoreQueryFilters().Where(r => r.At < before);
}

services.AddRetentionTarget<SensorReadingRetention>();
```

- Über alle Organisationen löschen: `IgnoreQueryFilters()`.
- `ExecuteDeleteInBatchesAsync` (`Coworkee.Infrastructure.Persistence`) löscht 1000 Zeilen pro Anweisung, damit keine Anweisung eine große Tabelle lange sperrt. Wie jedes `ExecuteDelete` umgeht es den Change Tracker: kein Audit-Eintrag, keine Realtime-Nachricht.
- Zeilen mit Blob: erst die Zeile löschen, dann den Blob.
- Einen weich gelöschten Typ, den dein Target selbst aufräumt, nimmt `services.AddRetentionOwnedType<T>()` aus `soft-deleted` heraus.
- Ein Target, das manche Zeilen gelöscht hat, andere aber nicht, wirft `RetentionIncompleteException(deleted, message)`; der Lauf merkt sich beides.

## Job und Grenzen

`coworkee-retention` läuft nach `Coworkee:Retention:Cron` (Standard `41 2 * * *`, UTC). Er arbeitet die Datenarten mit eingestellten Tagen nacheinander ab, jede in einem eigenen Scope; ein Fehler landet im Lauf und hält die anderen nicht auf. Ein Lauf, der mit seinem Prozess abgestürzt ist, gilt nach zwei Stunden als beendet; jeder Anspruch hat sein eigenes Token, so gibt ein verspäteter erster Lauf nie den Anspruch des Laufs frei, der übernommen hat.

- Die Tage gelten systemweit: Eigene Tage pro Organisation gibt es nicht.
- Es gibt kein Archiv: Gelöschte Daten sind weg.
- Die Job-Historie von Hangfire und die Instanzen von Elsa haben ihren eigenen Ablauf und stehen nicht auf der Seite; nie gesendete Chat-Anhänge behalten ihre stündliche Bereinigung.
