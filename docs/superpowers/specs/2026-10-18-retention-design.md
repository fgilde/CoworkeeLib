# Aufbewahrung (Retention) – Design

Lib `C:\dev\privat\github\coworkee` (main), Demo `C:\dev\privat\github\cleanarchitecturebaseblazor` (master), Sharemee `C:\dev\privat\github\shareme` (master). Regeln aus `CONTINUE.md` (CCD, `.razor` + `.razor.cs`, wenig Kommentare, `// ponytail:` für bewusste Abkürzungen, Docs EN + `.de.md`, kein Push).

## Warum
Viele Tabellen wachsen unbegrenzt (Audit-Trail, Outbox, Inbox, AI-Tool-Calls, Mail-Log, Benachrichtigungen, Import/Export-Jobs mit Dateiinhalt, gelöschte Dateien samt Blobs, Backups, OpenIddict-Tokens). Die wenigen Aufräumregeln, die es gibt, stehen verstreut in der Konfiguration (`WebhookOptions.LogRetentionDays`). Der Owner will eine zentrale Admin-Seite: pro Datenart „Tage behalten“ einstellen, sehen, wie viel betroffen wäre, und sofort löschen können. Dazu gibt es eine eigene Permission.

## Ziele
- Ein Modul meldet seine Datenarten selbst (`IRetentionTarget`). Neue Module und App-Code erscheinen damit automatisch auf der Seite.
- Ein nächtlicher Job räumt alles ab, was älter ist als eingestellt.
- Admin-Seite in Layout A (vom Owner gewählt): eine Tabelle, eine Zeile pro Datenart. Spalten: Daten, Behalten (Tage), Betroffen jetzt (live gezählt), Letzter Lauf, Jetzt löschen.
- Eine eigene Permission `Retention.Manage`.

## Nicht-Ziele
- Fristen pro Tenant. Outbox, Inbox und Tokens sind systemweit. Eigene Fristen pro Tenant gibt es erst, wenn jemand sie braucht (`ponytail:` im Code).
- Archivieren (Export vor dem Löschen).
- Hangfire-Job-Historie und Elsa-Instanzen: Beide haben ihre eigene Ablaufzeit.
- Chat-Anhänge, die nie gesendet wurden: Die bestehende stündliche Bereinigung bleibt; ihre Frist ist in Stunden angegeben.

## Anti-Ziele
- Ein Lib-Update löscht nicht überraschend Daten. Jede Datenart, die heute nie gelöscht wird, hat in der Lib den Default „nie“. Nur die Webhook-Logs behalten ihren heutigen Default von 30 Tagen.
- Kein Löschen, das große Tabellen sperrt: Gelöscht wird in Batches.

## Entscheidungen

### Abstraktion (Coworkee.Application, Ordner `Retention/`)
```csharp
public interface IRetentionTarget
{
    string Key { get; }            // "audit", "outbox", ...
    string Title { get; }          // englischer Text = Lokalisierungs-Key
    string? Description { get; }
    int? DefaultDays { get; }      // null = nie
    Task<long> CountAsync(DateTimeOffset before, CancellationToken ct);
    Task<long> DeleteAsync(DateTimeOffset before, CancellationToken ct);  // batcht selbst
}
services.AddRetentionTarget<T>();  // TryAddEnumerable scoped, wie AddSystemStatus<T>
```
`DeleteAsync` löscht immer über alle Tenants (`IgnoreQueryFilters()`) mit `ExecuteDeleteAsync` in Batches (Ids der ältesten 1000 auswählen und löschen, wiederholen, bis nichts mehr übrig ist). Targets mit Blobs (Dateien, Backups) löschen erst die Zeile und danach den Blob, wie `ChatAttachmentCleanup`.

### Neues Paket `Coworkee.Retention`
Enthält Modul, Runner, Endpoints, Settings, Permission und Lauf-Tabelle. Das Paket hängt an Infrastructure, BackgroundJobs, Settings und AspNetCore.
- **Settings:** Pro Target gibt es eine Setting `Retention.{Key}.Days` (Int, nur Scope Global, Default = `DefaultDays`). Gruppe `Retention` mit `page: "/admin/retention"`, damit sie nicht in den generischen Settings-Tabs auftaucht. Leer oder `0` heißt nie löschen. Der Default lässt sich per `Coworkee:Settings:Defaults:Retention.audit.Days` überschreiben (bestehender Mechanismus).
- **Tabelle `cw.RetentionRuns`:** `Key` (PK), `LastRunAt`, `LastDeleted`, `LastError`, `RunningSince`, `LastRunBy` (Guid?, null = Job).
- **Job:** Der Recurring Job `coworkee-retention` läuft mit Cron `{Section}:Cron`, Default `"41 2 * * *"`. Er arbeitet die Targets nacheinander ab, jedes in einem eigenen Scope. Ein Fehler wird in `LastError` geschrieben und beendet den Lauf nicht.
- **Endpoints** unter `/api/v1/retention`. Alle brauchen `Retention.Manage` und die Systemorganisation (403 `retention.system_only`).
  - `GET /`: Liste mit key, title, description, days, defaultDays, affected, lastRunAt, lastDeleted, lastError, running.
  - `GET /{key}/count?days=N`: Live-Zählung für das Eingabefeld.
  - `PUT /`: `{ key: days? }` schreibt die Settings.
  - `POST /{key}/run` mit Body `{ days }` (≥ 1): reiht einen Hangfire-Job ein und antwortet mit 202. Ein bereits laufender Key antwortet mit 409.
- **Sicherheit:** Manuelles Löschen verlangt im Client eine Bestätigung, die die Anzahl nennt. Das Log enthält den Benutzer, `LastRunBy` speichert ihn.
- **Permission:** Gruppe `Retention`, `Retention.Manage`. Administratoren bekommen sie wie die anderen Admin-Permissions (Seeding wie bei Backup oder Webhooks).

### Targets in der Lib
| Key | Modul | Daten | Spalte | Default |
|---|---|---|---|---|
| `audit` | Retention (Infrastructure-Tabelle) | `AuditEntries` | `OccurredAt` | nie |
| `snapshots` | Retention | `EntitySnapshots` | `CreatedAt` | nie |
| `outbox` | Retention | `OutboxMessages`, nur verarbeitete | `ProcessedAt` | nie |
| `soft-deleted` | Retention | alle `ISoftDelete`-Typen des EF-Modells außer Dateien | `DeletedAt` | nie |
| `inbox` | EventBus | `InboxMessages` | `ProcessedAt` | nie (entfernt den `ponytail:`-Kommentar) |
| `ai-tool-calls` | Ai | `AiToolCalls` | `At` | nie |
| `ai-sessions` | Ai | `AiChatSessions` (Nachrichten per Cascade) | `UpdatedAt` | nie |
| `webhook-deliveries` | Webhooks | `WebhookDeliveries`, ohne Pending | `CreatedAt` | `LogRetentionDays` (30), ersetzt `WebhookLogCleanup` |
| `mail-log` | Mailing | `OutgoingMails`, ohne Queued | `QueuedAt` | nie |
| `notifications` | Notifications | `Notifications` | `CreatedAt` | nie |
| `data-transfer-jobs` | OData | `DataTransferJobs`, beendete | `FinishedAt` | nie |
| `deleted-files` | Files | soft-gelöschte `StoredFiles`/`FileFolders` + Blobs | `DeletedAt` | nie |
| `backups` | Backup | `DatabaseBackups` + Blobs | `CreatedAt` | nie |
| `auth-tokens` | AuthServer (Store) | OpenIddict-Tokens/-Autorisierungen, über `PruneAsync` | Erstellung | nie |

- **`soft-deleted`:** Läuft per Reflection über das EF-Modell, ein Typ nach dem anderen. Scheitert ein Typ an einem Fremdschlüssel, wird das in `LastError` vermerkt und der nächste Typ ist dran.
- **`audit`:** Die Beschreibung weist darauf hin, dass Entity-Historie und Wiederherstellen auf dem Audit-Trail beruhen.
- **`auth-tokens`:** `CountAsync` zählt die Tokens, die `PruneAsync` entfernen würde, also ungültige oder abgelaufene vor dem Stichtag. Wo das zu teuer ist, darf `null` zurückkommen und die Seite zeigt „–“. Die Schnittstelle gibt dann `long?` zurück; entscheidet der Builder.
- **Webhooks:** `WebhookOptions.LogRetentionDays` bleibt als Default der Setting. `WebhookLogCleanup` entfällt.
- **Hosts ohne Retention-Modul:** Die Targets werden registriert, aber nie ausgeführt. In dem Fall behält Webhooks seine eigene Bereinigung. Damit sie nicht doppelt läuft, entscheidet der Builder; Vorschlag: Webhooks registriert `WebhookLogCleanup` nur, wenn kein Retention-Modul geladen ist.

### Client (Coworkee.Client.Blazor)
- Library-Seite mit Key `retention`, Route `admin/retention`.
- Nav-Eintrag „Retention“ in der Gruppe Monitoring, `HostOnly`, Permission `Retention.Manage`.
- Komponenten `Pages/Admin/Retention.razor` + `.razor.cs` mit `PageHeader`.
- Tabelle als `MudDataGrid` mit `Items`, wie bei Backups. Das Tage-Feld ist ein `MudNumericField`, leer heißt nie. Die Zählung läuft debounced über `GET count`.
- Buttons: „Speichern“ für alle Zeilen, „Jetzt löschen“ pro Zeile mit Bestätigungsdialog („12.480 Einträge älter als 365 Tage löschen?“).
- Läuft ein Target, zeigt die Zeile einen Fortschrittsindikator, und die Seite fragt alle 2 s nach, bis es fertig ist.
- `ICoworkeeApi` bekommt die neuen Methoden. Texte kommen in alle Sprachdateien.
- Alle Admin-Seiten nutzen die MudEx-Dialoge und -Animationen wie die übrigen.

### Systemstatus
Ein `RetentionStatus`-Contributor zeigt pro Target den letzten Lauf und die Fehler. Er verlinkt auf die Seite.

### Template, CLI, Studio
- CLI- und Studio-Option `retention` (Bool, Gruppe Modules, Default an, Paket `Coworkee.Retention`).
- Die Template-`appsettings.json` neuer Apps setzt technische Defaults: `outbox` 14, `inbox` 14, `auth-tokens` 14, `data-transfer-jobs` 30. Alles andere bleibt auf nie.
- Template-Migration neu erzeugen (`update-migrations.ps1`).

### Doku und Release
- `website/docs/modules/retention.md` + `.de.md`: Targets-Tabelle, eigenes Target schreiben (Codebeispiel), Settings-Keys, Cron und Limits. Eintrag in `mkdocs.yml` samt `nav_translations`.
- Webhooks-Doku verweist auf Retention.
- Changelog *Unreleased*: Neues Modul. **Migration nötig**, wenn das Modul geladen ist.
- Upgrade-Regel (Paket hinzufügen ist optional, `manual`-Hinweis).
- Demo und Sharemee: Paket und Modul einbinden, Migration.

## Dem Builder überlassen
Batchgröße, genaue DTO-Namen, ob `CountAsync` `long?` liefert, wie das Doppellaufen der Webhook-Bereinigung vermieden wird, Icons. Tests: Provider-Matrix (Postgres, SQL Server, SQLite) für Runner und Targets, bUnit-Tests für die Seite, ein Test, dass jedes registrierte Target in der Liste erscheint.
