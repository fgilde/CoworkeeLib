# Datenschutz und personenbezogene Daten

Jedes Modul, das Daten über einen Benutzer speichert, liefert sie über `IPersonalDataContributor`: einen benannten JSON-Abschnitt für den Export und einen Löschschritt für das Löschen des Kontos.

```csharp
internal sealed class TaskPersonalData(CoworkeeDbContext db) : IPersonalDataContributor
{
    public string Section => "tasks";

    public async Task<object?> ExportAsync(PersonalDataSubject subject, CancellationToken ct) =>
        await db.Set<TaskItem>().Where(t => t.AssigneeId == subject.UserId).Select(t => new { t.Title, t.DueAt }).ToListAsync(ct);

    public Task EraseAsync(PersonalDataSubject subject, CancellationToken ct) =>
        db.Set<TaskItem>().Where(t => t.AssigneeId == subject.UserId)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.AssigneeId, (Guid?)PersonalDataSubject.ErasedUserId), ct);
}

services.AddScoped<IPersonalDataContributor, TaskPersonalData>();
```

`PersonalDataSubject` enthält Benutzer-ID, Mandant und E-Mail-Adresse und wird gelesen, bevor ein Contributor läuft; die Reihenfolge spielt also keine Rolle. Ein Abschnitt, der `null` liefert, fehlt im Export. Das Löschen läuft in einer Transaktion; direkte Aufrufe von `ExecuteDelete`/`ExecuteUpdate` schreiben keine Audit-Einträge mit den alten Werten.

## Die Regeln des Löschens

- **Seine eigenen Daten gehen**: was nur seinetwegen existiert und nur ihm dient (Profil, Einstellungen, Benachrichtigungen, Schlüssel, Sitzungen, private Berichte, seine Jobs und Zeitpläne, seine Chat-Nachrichten und Kommentare).
- **Datensätze der Organisation bleiben**, mit `PersonalDataSubject.ErasedUserId` (`00000000-0000-0000-0000-000000000000`) an der Stelle seiner ID: „ein gelöschter Benutzer", anders als `null`, „niemand". Freitext, den er hineingeschrieben hat (Workflow-Kommentare), geht.
- **Nichts läuft mehr als er**: Berichtsabos, geplante Exporte und laufende Exporte stoppen, Webhook-Endpunkte, die ihm gehörten, werden abgeschaltet, seine Adresse verschwindet aus den Empfängern anderer Zeitpläne, auf ihn wartende Freigaben gehen weiter, seine Tokens, API-Schlüssel, Links und der zwischengespeicherte Sitzungsstempel verschwinden.
- **Organisationen**: Eine Benutzer-ID gehört zu einem Benutzer einer Organisation (`Users.TenantId`); Zeilen mit seiner ID sind also seine, wo immer sie liegen (die ID eines Host-Administrators an den Organisationen, die er angelegt hat). Zeilen, die über seine **E-Mail-Adresse** gefunden werden, zählen nur in seiner Organisation: Dieselbe Adresse kann einem anderen Benutzer einer anderen Organisation gehören.

Nach den Contributors speichert die Bibliothek einmal und führt dann ihren eigenen Schritt (`PersonalDataReferences`, Coworkee.Infrastructure) über die ganze Datenbank aus:

| Was | Regel |
|---|---|
| `CreatedBy`, `ModifiedBy`, `DeletedBy` jeder Tabelle (auch Ihrer) | seine ID wird zum Platzhalter |
| `AuditEntries.ActorId`, `ImpersonatorId` | seine ID wird zum Platzhalter |
| Audit-Einträge der mit ihm gelöschten Datensätze (sein Benutzer, Zeilen mit seiner ID im Schlüssel, Datensätze, die er angelegt hat und die nicht mehr existieren) | jeder erfasste Wert wird `***`; Aktion, Zeit, Datensatztyp und ID bleiben |
| Alle anderen Audit-Einträge | seine ID (auch in Kurzform) und seine E-Mail-Adresse in erfassten Werten werden ersetzt (Platzhalter, `***`) |
| `EntitySnapshots` | Snapshots der mit ihm gelöschten Datensätze werden gelöscht; in den anderen werden ID und Adresse ersetzt; `CreatedBy` wird zum Platzhalter |
| `OutboxMessages.ActorId`, `ImpersonatorId` | seine ID wird zum Platzhalter (wartende Nachrichten laufen trotzdem, als niemand Bestimmtes) |
| `AccessTokens` (anonyme Links, die er ausgestellt hat) | gelöscht |

Danach wird der zwischengespeicherte Sicherheitsstempel auf jeder Instanz verworfen, damit Tokens des Benutzers sofort nicht mehr gelten.

### Der Audit-Verlauf

Der Verlauf beantwortet, *was mit welchem Datensatz wann geschah, und ob eine Person oder das System es tat*. Das bleibt: Es ist der Nachweis der Organisation, und ihn zu löschen ließe ein Löschen Änderungen verbergen. *Wer* die Person war und was der Verlauf über sie festgehalten hat, geht: Seine ID wird zum Platzhalter, und die Werte seiner eigenen Datensätze (sein Benutzer, seine Mitgliedschaften, die Kommentare, Nachrichten, Einstellungen und andere Datensätze, die er angelegt hat und die nicht mehr existieren) werden `***`. Werte von Datensätzen, die bleiben (ein Auftrag, den er bearbeitet hat), bleiben, ohne seine ID und Adresse. Namen des Benutzers, die andere als Freitext eingegeben haben, werden nicht gesucht.

## Eingebaute Abschnitte

| Abschnitt | Modul | Im Export | Löschen |
|---|---|---|---|
| `profile` | Identity | Name, E-Mail, Telefon, Adresse, Bild, Zwei-Faktor an oder aus, Rollen, Gruppen, externe Anmeldungen, Passkeys (Name, Datum, Sicherungsstatus), `claims`, eigene `permissionGrants`, `resourcePermissions` (Typ, ID, Rolle), `selectedGroups` | Benutzer gelöscht samt Rollen, Claims, Anmeldungen, Tokens, Passkeys, Mitgliedschaften und Gruppenauswahl (Kaskade); eigene Berechtigungen und Ressourcen-Berechtigungen gelöscht |
| `sessions` | Identity | [Sitzungen](clients.md#sitzungen) (App, Gerät, Browser, User-Agent, IP-Adresse, Beginn, letzte Aktivität, Ende und Grund) | seine Sitzungen gelöscht; Sitzungen anderer, als die er angemeldet war oder die er beendet hat, behalten das mit dem Platzhalter |
| `apiKeys` | Identity | [API-Schlüssel](api-keys.md) (Name, Präfix, Berechtigungen, Daten, letzte Nutzung), nie der Hash | seine Schlüssel gelöscht; Schlüssel anderer, die er widerrufen hat, behalten den Widerruf mit dem Platzhalter |
| `signIns` | Auth-Server | freigegebene Clients | Freigaben und Tokens gelöscht, jede Sitzung endet |
| `settings` | Einstellungen | Werte mit Benutzerbereich (Geheimnisse als `***`) | gelöscht |
| `notifications` | Benachrichtigungen | Benachrichtigungen, Geräte mit Web Push, Einstellungen, gefolgte Datensätze, auf ihre Verzögerung wartende Mails, letzter Digest | gelöscht, in jeder Organisation |
| `mails` | Mailversand | Mails an die Adresse in seiner Organisation, ohne Inhalt | samt Anhängen gelöscht, in seiner Organisation |
| `chat` | Chat | `messages` (überall gesendet, in Direkt-Chats empfangen), `memberships`, `reactions`, `attachments` (aufgelistet, ohne Inhalt) | gesendete Nachrichten samt Dateien, Reaktionen und Mitgliedschaften gelöscht; Direkt-Chats mit beiden Seiten gelöscht; Gruppen-Chats, die er angelegt hat, bleiben mit dem Platzhalter als Ersteller |
| `social` | Kommentare, Tags, Bewertungen, Reaktionen | eigene Kommentare, Bewertungen und Reaktionen | gelöscht, Antworten auf die Kommentare ebenso |
| `aiToolCalls`, `aiConversations` | KI | Tool-Aufrufe (Tool, Kanal, Eingabe, Ergebnis), Unterhaltungen | gelöscht |
| `activity` | Audit | was der Benutzer geändert hat, selbst oder als anderer Benutzer angemeldet (`asOtherUser`) | Verlauf bleibt wie oben beschrieben |
| `securityLog` | Identity | seine Einträge im [Sicherheitsprotokoll](../modules/auditing.md#sicherheitsprotokoll): Anmeldungen, Sperren, Änderungen an Passwort, E-Mail, Schlüsseln und Zugriffen (Zeit, Aktion, Ergebnis, Grund, Adresse, Browser, Details) | Einträge bleiben mit dem Platzhalter für ihn als Benutzer, Handelnden oder Angemeldeten; Adresse, Browser und Details werden entfernt |
| `requests` | Audit | seine letzten 1000 Aufrufe im [Request-Log](../modules/auditing.md#request-log) (Zeit, Methode, Pfad, Status, Adresse, Browser; keine Header oder Inhalte) | Zeilen bleiben mit dem Platzhalter; Adresse, Browser, Header und Inhalte werden entfernt |
| `files` | Dateien | hochgeladene Dateien (Name, Typ, Größe) | bleiben mit dem Platzhalter als hochladender Person; seine Registrierungsunterlagen (`Registrations/{user}`) samt Inhalt und Ordner gelöscht |
| `attachments` | Anhänge an Datensätzen | angehängte Dateien (Datensatz, Slot, Name, Typ, Größe) | bleiben mit dem Platzhalter als hochladender Person |
| `reports` | Berichte | eigene `reports`, `favorites`, `subscriptions` (Zeitplan, Filter), `dashboard` | private Berichte, Favoriten, Abos und Dashboard-Wahl gelöscht; geteilte Berichte bleiben für ihr Publikum, Eigentümer ist der Platzhalter (niemand ändert sie mehr) |
| `dataTransfers` | Import und Export | `jobs` (Dateien mit Name und Größe aufgelistet), eigene `schedules`, `mailedBy` (Zeitpläne anderer, die ihm mailen) | Jobs samt Dateien gelöscht (ein laufender Export stoppt), seine Zeitpläne gelöscht, seine Adresse aus den Empfängern der anderen in seiner Organisation entfernt |
| `webhooks` | Webhooks | Endpunkte, die er zuletzt gespeichert hat (URL, Ereignisse, Zustand) | bleiben, abgeschaltet und im Besitz des Platzhalters, da Zustellungen mit seinen Berechtigungen liefen; erneutes Speichern macht den Administrator zum Eigentümer |
| `workflows` | Workflows | ausgeführte `steps` (mit Kommentaren), `decisions`, angeforderte Freigaben (`requested`) | Verlauf und Entscheidungen bleiben mit dem Platzhalter, seine Kommentare werden entfernt; er verlässt offene Freigaben, und eine, die danach auf niemanden mehr wartet, ist sofort überfällig (der Fristen-Job setzt sie in ihren Timeout-Zustand oder holt die Eskalationsrollen dazu; eine bereits eskalierte wird abgebrochen) |
| keiner | Aufbewahrung | | von Hand gestartete Bereinigungen behalten den Platzhalter als `LastRunBy` |

`tests/Coworkee.ArchitectureTests/PersonalDataCoverageTests` führt jede Spalte der Tabellen der Bibliothek, die einen Benutzer nennt, mit dem Contributor, der sie behandelt; eine neue Spalte lässt den Test scheitern, bis sie abgedeckt ist.

## Was bleibt

| Daten | Warum | Bis |
|---|---|---|
| Audit-Verlauf und Snapshots, wie oben beschrieben | Nachweis über die Datensätze der Organisation | die [Aufbewahrungs](../modules/retention.md)-Ziele `audit` und `snapshots` |
| Inhalte verarbeiteter Outbox-Nachrichten (Domain Events) | Zustellprotokoll des Event-Busses | das Ziel `outbox` |
| Webhook-Zustellprotokolle (an Empfänger gesendete Inhalte) | was das System an Dritte verlassen hat | das Ziel `webhook-deliveries` |
| Benachrichtigungen, Chat-Nachrichten und Kommentare anderer, die ihn im Text nennen | sie gehören den anderen Benutzern | das Ziel `notifications` bzw. solange die anderen sie behalten |
| Elsa-Workflow-Instanzen (ihre Eingabe enthält die Benutzer-ID) | laufende Workflows würden brechen | Elsas eigenes Aufräumen beendeter Instanzen |
| Dokumente im Suchindex | der Index gehört der App (`ISearchIndex`) | der Contributor der App entfernt sie |
| Datenbank-Backups | Backups werden als Ganzes zurückgespielt | sie werden rotiert |

## Ihre eigenen Tabellen

- `CreatedBy`, `ModifiedBy` und `DeletedBy` (vom Typ `Guid`) werden für jede Tabelle des Modells behandelt; nichts zu tun.
- Jede andere Spalte mit Benutzer-ID, Name oder Adresse eines Benutzers braucht einen Contributor in Ihrem Modul: exportieren, löschen, was seins ist, und `PersonalDataSubject.ErasedUserId` in den Rest schreiben. Stoppen, was als der Benutzer läuft (eigene Jobs, Abos).
- Zeilen, die über die E-Mail-Adresse gefunden werden: auf `subject.TenantId` beschränken.
- Dokumente im Suchindex mit personenbezogenen Daten: im Contributor entfernen.

## Endpunkte

| Endpunkt | Wer | Wirkung |
|---|---|---|
| `GET /api/v1/identity/me/personal-data` | der Benutzer | JSON-Objekt mit einer Eigenschaft pro Abschnitt, nach Namen sortiert |
| `POST /api/v1/identity/me/delete` mit `{ "email": "…" }` | der Benutzer | löscht alles; die Adresse muss zum Konto passen (sonst `400`) |
| `DELETE /api/v1/identity/users/{id}` | `Identity.Users.Manage` | dasselbe für einen anderen Benutzer des Mandanten; nur Administratoren löschen Administratoren (`403 identity.admin_role_restricted`), niemand löscht einen Benutzer, der eine Berechtigung hat, die dem Aufrufer fehlt (`403 identity.user_more_permissions`) |

Der letzte aktive Administrator lässt sich nicht löschen (`409 identity.last_admin`). Keiner dieser Requests steht dem KI-Assistenten zur Verfügung. Wer als anderer Benutzer angemeldet ist ([Anmelden als](../features/identity.md#als-anderer-benutzer-anmelden)), kann weder dessen Daten exportieren noch das Konto löschen (`403 auth.impersonating`).

Der Export entsteht synchron: Die Daten eines Benutzers sind klein, und es bleibt nichts im Speicher zurück, das eigenen Ablauf und eigenes Aufräumen bräuchte. Erst wenn einzelne Benutzer viele Megabyte erreichen, lohnt sich ein Hintergrundjob. Dateien werden aufgelistet, nicht mitgeliefert: Uploads und Anhänge gehören zu den Datensätzen der Organisation, Exportdateien enthalten Zeilen der Organisation.

## Im Client

Die Profilseite hat einen Reiter **Datenschutz**: Daten als `personal-data.json` herunterladen oder das Konto löschen, nachdem die eigene E-Mail-Adresse eingegeben wurde; danach meldet der Client ab. Administratoren finden **Delete user** auf der Detailseite eines Benutzers.
