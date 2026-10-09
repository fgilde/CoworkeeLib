# Datenschutz und personenbezogene Daten

Jedes Modul, das Daten über einen Benutzer speichert, liefert sie über `IPersonalDataContributor`: einen benannten JSON-Abschnitt für den Export und einen Löschschritt für das Löschen des Kontos.

```csharp
internal sealed class TaskPersonalData(CoworkeeDbContext db) : IPersonalDataContributor
{
    public string Section => "tasks";

    public async Task<object?> ExportAsync(PersonalDataSubject subject, CancellationToken ct) =>
        await db.Set<TaskItem>().Where(t => t.AssigneeId == subject.UserId).Select(t => new { t.Title, t.DueAt }).ToListAsync(ct);

    public Task EraseAsync(PersonalDataSubject subject, CancellationToken ct) =>
        db.Set<TaskItem>().Where(t => t.AssigneeId == subject.UserId).ExecuteUpdateAsync(s => s.SetProperty(t => t.AssigneeId, (Guid?)null), ct);
}

services.AddScoped<IPersonalDataContributor, TaskPersonalData>();
```

`PersonalDataSubject` enthält Benutzer-ID, Mandant und E-Mail-Adresse und wird gelesen, bevor ein Contributor läuft; die Reihenfolge spielt also keine Rolle. Ein Abschnitt, der `null` liefert, fehlt im Export. Das Löschen läuft in einer Transaktion; direkte Aufrufe von `ExecuteDelete`/`ExecuteUpdate` schreiben keine Audit-Einträge mit den alten Werten.

## Eingebaute Abschnitte

| Abschnitt | Modul | Löschen |
|---|---|---|
| `profile` | Identity: Name, E-Mail, Telefon, Adresse, Bild, Rollen, Gruppen, externe Anmeldungen | Benutzer gelöscht (Rollen, Claims, Anmeldungen, Mitgliedschaften per Kaskade), eigene Berechtigungen entfernt, Werte im Audit-Verlauf des Benutzers durch `***` ersetzt |
| `settings` | Einstellungen: Werte mit Benutzerbereich (Geheimnisse als `***`) | gelöscht |
| `notifications` | Benachrichtigungen | gelöscht, samt Digest-Status |
| `chat` | Chat: gesendete und empfangene Nachrichten | beide Seiten der Unterhaltungen gelöscht |
| `aiToolCalls` | KI: Protokoll der Tool-Aufrufe (der Assistent speichert keinen Chatverlauf) | gelöscht |
| `mails` | Mailversand: Mails an die Adresse, ohne Inhalt | gelöscht |
| `activity` | Audit: was der Benutzer geändert hat | bleibt; der Verlauf nennt den Handelnden nur per ID, die danach auf niemanden mehr zeigt |
| `signIns` | Auth-Server: freigegebene Clients | Freigaben und Tokens gelöscht, jede Sitzung endet |

Datenbank-Backups behalten frühere Daten, bis sie rotiert werden.

## Endpunkte

| Endpunkt | Wer | Wirkung |
|---|---|---|
| `GET /api/v1/identity/me/personal-data` | der Benutzer | JSON-Objekt mit einer Eigenschaft pro Abschnitt |
| `POST /api/v1/identity/me/delete` mit `{ "email": "…" }` | der Benutzer | löscht alles; die Adresse muss zum Konto passen (sonst `400`) |
| `DELETE /api/v1/identity/users/{id}` | `Identity.Users.Manage` | dasselbe für einen anderen Benutzer des Mandanten; nur Administratoren löschen Administratoren |

Der letzte aktive Administrator lässt sich nicht löschen (`409 identity.last_admin`). Keiner dieser Requests steht dem KI-Assistenten zur Verfügung.

Der Export entsteht synchron: Die Daten eines Benutzers sind klein, und es bleibt nichts im Speicher zurück, das eigenen Ablauf und eigenes Aufräumen bräuchte. Erst wenn einzelne Benutzer viele Megabyte erreichen, lohnt sich ein Hintergrundjob.

## Im Client

Die Kontoseite hat einen Reiter **Datenschutz**: Daten als `personal-data.json` herunterladen oder das Konto löschen, nachdem die eigene E-Mail-Adresse eingegeben wurde; danach meldet der Client ab. Administratoren finden **Delete user** auf der Detailseite eines Benutzers.
