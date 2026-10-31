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
| `profile` | Identity: Name, E-Mail, Telefon, Adresse, Bild, Zwei-Faktor an oder aus, Rollen, Gruppen, externe Anmeldungen, Passkeys (Name, Datum, Sicherungsstatus) | Benutzer gelöscht (Rollen, Claims, Anmeldungen, Tokens, Passkeys, Mitgliedschaften per Kaskade), eigene Berechtigungen und Ressourcen-Berechtigungen entfernt, Werte im Audit-Verlauf des Benutzers durch `***` ersetzt |
| `settings` | Einstellungen: Werte mit Benutzerbereich (Geheimnisse als `***`) | gelöscht |
| `notifications` | Benachrichtigungen: die Benachrichtigungen und die Geräte mit Web Push | gelöscht, samt Digest-Status und Push-Geräten |
| `chat` | Chat: gesendete Nachrichten, empfangene Direktnachrichten | gesendete Nachrichten samt Dateien, Reaktionen und Gruppenmitgliedschaften gelöscht; Direkt-Chats mit beiden Seiten gelöscht |
| `aiToolCalls` | KI: was der Benutzer den Assistenten und MCP-Clients tun ließ (Tool, Kanal, Eingabe, Erfolg oder Fehler) | gelöscht |
| `aiConversations` | KI: die Unterhaltungen mit dem Assistenten (Titel, Nachrichten) | gelöscht |
| `apiKeys` | Identity: [API-Schlüssel](api-keys.md) (Name, Präfix, Berechtigungen, Daten, letzte Nutzung), nie der Hash | gelöscht |
| `mails` | Mailversand: Mails an die Adresse, ohne Inhalt | gelöscht |
| `activity` | Audit: was der Benutzer geändert hat | bleibt; der Verlauf nennt den Handelnden nur per ID, die danach auf niemanden mehr zeigt |
| `social` | Kommentare, Tags, Bewertungen und Reaktionen: eigene Kommentare, Bewertungen und Reaktionen | gelöscht, Antworten auf die Kommentare ebenso |
| `files` | Dateien: hochgeladene Dateien (Name, Typ, Größe) | bleiben, sie gehören in die Ordner der Organisation |
| `attachments` | Anhänge an Datensätzen: angehängte Dateien (Datensatz, Slot, Name, Typ, Größe) | bleiben, sie gehören zu den Datensätzen der Organisation |
| `signIns` | Auth-Server: freigegebene Clients | Freigaben und Tokens gelöscht, jede Sitzung endet |

Datenbank-Backups behalten frühere Daten, bis sie rotiert werden.

## Endpunkte

| Endpunkt | Wer | Wirkung |
|---|---|---|
| `GET /api/v1/identity/me/personal-data` | der Benutzer | JSON-Objekt mit einer Eigenschaft pro Abschnitt, nach Namen sortiert |
| `POST /api/v1/identity/me/delete` mit `{ "email": "…" }` | der Benutzer | löscht alles; die Adresse muss zum Konto passen (sonst `400`) |
| `DELETE /api/v1/identity/users/{id}` | `Identity.Users.Manage` | dasselbe für einen anderen Benutzer des Mandanten; nur Administratoren löschen Administratoren (`403 identity.admin_role_restricted`) |

Der letzte aktive Administrator lässt sich nicht löschen (`409 identity.last_admin`). Keiner dieser Requests steht dem KI-Assistenten zur Verfügung. Wer als anderer Benutzer angemeldet ist ([Anmelden als](../features/identity.md#als-anderer-benutzer-anmelden)), kann weder dessen Daten exportieren noch das Konto löschen (`403 auth.impersonating`).

Der Export entsteht synchron: Die Daten eines Benutzers sind klein, und es bleibt nichts im Speicher zurück, das eigenen Ablauf und eigenes Aufräumen bräuchte. Erst wenn einzelne Benutzer viele Megabyte erreichen, lohnt sich ein Hintergrundjob.

## Im Client

Die Profilseite hat einen Reiter **Datenschutz**: Daten als `personal-data.json` herunterladen oder das Konto löschen, nachdem die eigene E-Mail-Adresse eingegeben wurde; danach meldet der Client ab. Administratoren finden **Delete user** auf der Detailseite eines Benutzers.
