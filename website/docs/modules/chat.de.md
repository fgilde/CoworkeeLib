# Chat

`Coworkee.Chat` ergänzt Direkt- und Gruppenchats zwischen den Personen einer Organisation: formatierte Nachrichten, Erwähnungen von Personen und Datensätzen, Antworten, Reaktionen, Dateien und Bilder, Bearbeiten und Löschen, Lesebestätigungen und wer gerade schreibt. Alles erreicht die Mitglieder über den [Realtime-Hub](realtime.md) auf ihrem Topic `user:{id}`, jeder offene Tab aktualisiert sich sofort.

![Chat-Seite](../assets/screenshots/chat-light.webp){ .shot }

```csharp
[DependsOn(typeof(CoworkeeChatModule))]
public sealed class MyAppDatabaseModule : CoworkeeModule;
```

Das Modul hängt von den Modulen Identity, Realtime und Storage ab (Dateien landen im [Blob-Speicher](storage.md)). Erwähnungen benachrichtigen über das Benachrichtigungsmodul (siehe [Realtime und Benachrichtigungen](realtime.md#benachrichtigungen)), wenn die App es hat.

## Schreiben

- Das Eingabefeld ist der `RichTextEditor` der Bibliothek (TipTap oder Quill, wie der Admin es in der Einstellung *Rich-Text-Editor* gewählt hat, siehe [Mailing](mailing.md#vorlage-bearbeiten)) mit kurzer Werkzeugleiste: fett, kursiv, durchgestrichen, Code, Zitat, Listen, Link. TipTap versteht beim Tippen auch Markdown-Kürzel (`**fett**`, `*kursiv*`, `- ` Liste, `> ` Zitat, `` ` `` Code).
- **Enter** sendet, **Umschalt+Enter** bricht die Zeile um. Ist eine Erwähnungsliste offen, wählt Enter den Eintrag.
- **`@`** schlägt die Personen der Organisation vor. Eine Erwähnung verlinkt auf die Seite der Person (`/people/{id}`, Seitenschlüssel `person`) und zeigt immer ihren aktuellen Namen.
- **`#`** schlägt Datensätze aus der [globalen Suche](../web/global-search.md) vor (jeder Provider, den der Schreibende nutzen darf, außer Chats). Die Erwähnung verlinkt auf die Seite des Datensatzes. Jeder Leser sieht sie nur, wenn seine eigene Suche den Datensatz findet; sonst erscheint sie als *#eingeschränkt* mit Schloss, ohne Bezeichnung und Link. Eine Erwähnung eines Datensatzes, den der Schreibende nicht sehen darf, wird abgelehnt.
- **Dateien**: über die Büroklammer, per Drag-and-drop auf das Eingabefeld oder durch Einfügen eines Bildes. Uploads zeigen ihren Fortschritt und öffnen sich im Datei-Viewer, sobald sie oben sind. Bilder erscheinen direkt als kleines WebP, das der Server erzeugt (`/thumbnail`, höchstens 480 px, aufrecht gedreht), andere Dateien als Chip; ein Klick öffnet die Datei in `MudExFileDisplay` (siehe [Dateien anzeigen](files.md#dateien-anzeigen)). Nur Mitglieder des Chats laden sie (`GET /api/v1/chat/attachments/{id}`), ausgeliefert wie im Dateimodul (kein Sniffing, inline in einer Sandbox). Hochgeladene, aber nie gesendete Dateien werden nach `Coworkee:Chat:UnsentAttachmentLifetime` gelöscht (Standard `1.00:00:00`, ein Job prüft stündlich).
- **Antworten**, **reagieren** (👍 ❤️ 😂 🎉 😮 😢, ein Klick auf eine Reaktion fügt die eigene hinzu oder nimmt sie zurück), **bearbeiten** und **löschen** stehen im Menü jeder Nachricht. Nur der Absender bearbeitet oder löscht; eine bearbeitete Nachricht zeigt *bearbeitet*, eine gelöschte *Diese Nachricht wurde gelöscht.* und verliert Dateien und Reaktionen.
- Die Häkchen unter einer eigenen Nachricht zeigen, wer sie gelesen hat (Tooltip mit Namen).
- Mit der Lupe suchst du in einem Chat: Nachrichten, die jedes Wort enthalten.

Der Server bereinigt jede Nachricht: Es bleiben nur Formatierung, Listen, Zitate, Code und Links (`http`, `https`, `mailto`, öffnen in einem neuen Tab); Skripte, Styles, Bilder und Event-Attribute fallen weg. Erwähnungen werden in einer festen Form gespeichert und für jeden Leser gerendert.

## Gruppenchats

*Neue Gruppe* fragt nach einem Namen und den Personen. Jedes Mitglied benennt die Gruppe um und fügt Personen hinzu; der Ersteller (Besitzer) entfernt andere; jeder kann austreten. Einen Direkt-Chat gibt es pro Paar genau einmal, er behält seine zwei Personen.

## Mini-Chats

![Mini-Chat-Dock](../assets/screenshots/chat-dock-light.webp){ .shot }

Chats öffnen sich auch als Mini-Chats in einem Dock unten rechts, links neben dem Assistenten-Button, bis zu drei gleichzeitig:

- die Kopfzeile minimiert ein Fenster auf seine Leiste (mit der Zahl neuer Nachrichten), *Im Chat öffnen* holt es auf die Chatseite, das Kreuz schließt es;
- die offenen Fenster merkt sich der Browser pro Benutzer, sie kommen nach einem Neuladen wieder;
- auf dem Handy füllt ein offener Mini-Chat den Bildschirm.

So öffnest du einen: *Als Mini-Chat öffnen* auf der Chatseite, die Befehlspalette (**Chatten mit…** wählt eine Person; `@name` listet passende Personen direkt), ein Klick auf den Avatar einer anderen Person (*Chat* oder *Profil*), der Button *Chat* auf der Personenseite, die Snackbar einer neuen Nachricht und eine Erwähnungs-Benachrichtigung. Ohne Dock auf dem Bildschirm (eigenes Layout) führen diese auf die Chatseite.

`ChatDock` (scoped) öffnet Chats aus eigenen Komponenten:

```csharp
@inject ChatDock Dock
<MudButton OnClick="() => Dock.OpenWithAsync(customer.ContactUserId)">Chat</MudButton>
```

## Erwähnungen und Benachrichtigungen

Eine Erwähnung benachrichtigt nur **Mitglieder** des Chats (Typ `chat.mention`, Link `/chat?c={id}`, der den Mini-Chat öffnet). Wer außerhalb des Chats erwähnt wird, bekommt einen Link auf seine Person, aber keine Benachrichtigung: Er könnte die Nachricht ohnehin nicht lesen. Neue Nachrichten ohne Erwähnung zeigen eine Snackbar in der App (keine dauerhafte Benachrichtigung).

## Einstellungen und Erweiterungen

| Einstellung | |
|---|---|
| `Chat.MaxAttachmentMb` | größte Datei in MB, Standard 25 (Global, für den Client sichtbar, der vor dem Upload prüft) |

Ein Virenscanner oder eine andere Prüfung klinkt sich als `IChatAttachmentScanner` ein; sagt ein Scanner nein, wird der Upload abgelehnt:

```csharp
services.AddScoped<IChatAttachmentScanner, ClamAvScanner>();
```

## API

| Endpunkt | |
|---|---|
| `GET /api/v1/chat/chats` | die Chats des Benutzers, neueste zuerst, mit Mitgliedern, letzter Nachricht und Zahl ungelesener |
| `POST /api/v1/chat/chats` | legt eine Gruppe an `{ "name", "memberIds" }` |
| `POST /api/v1/chat/chats/direct/{userId}` | öffnet den Direkt-Chat mit einer Person (beim ersten Mal angelegt) |
| `GET /api/v1/chat/chats/{id}` | ein Chat |
| `PUT /api/v1/chat/chats/{id}/name` | benennt eine Gruppe um |
| `POST /api/v1/chat/chats/{id}/members` · `DELETE …/members/{userId}` | Personen hinzufügen · entfernen oder austreten |
| `GET /api/v1/chat/chats/{id}/messages?before=&search=` | 50 Nachrichten, neueste zuletzt |
| `POST /api/v1/chat/chats/{id}/messages` | sendet `{ "html", "replyToId", "attachmentIds" }` |
| `POST /api/v1/chat/chats/{id}/attachments?name=` | lädt eine Datei hoch (Body = Datei, `Content-Type` = ihr Typ), danach ihre Id mitsenden |
| `POST /api/v1/chat/chats/{id}/read` · `…/typing` | als gelesen markieren · den anderen zeigen, dass du schreibst |
| `GET` · `PUT` · `DELETE /api/v1/chat/messages/{id}` | lesen (für den Leser gerendert) · bearbeiten · löschen |
| `POST /api/v1/chat/messages/{id}/reactions` | `{ "emoji" }` hinzufügen oder zurücknehmen |
| `GET /api/v1/chat/attachments/{id}?download=` | eine Datei, nur für Mitglieder |
| `GET /api/v1/chat/attachments/{id}/thumbnail` | das kleine WebP eines Bildes (`hasThumbnail` des Anhangs), nur für Mitglieder |
| `GET /api/v1/chat/unread-count` | ungelesene Nachrichten aller Chats |
| `GET /api/v1/chat/contacts`, `GET`/`POST /api/v1/chat/conversations/{userId}`, `POST …/{userId}/read` | die Klartext-API des Direkt-Chats pro Person (AirWorks-Chat-App, Assistent) |

`ChatMessageDto.Html` ist sicheres HTML zum Anzeigen; `Text` ist Klartext (Datensatz-Erwähnungen nur als `#`) für Vorschauen, Benachrichtigungen und die Suche. Der Assistent kann Chats auflisten, Nachrichten lesen und senden ([KI-Werkzeuge](ai.md)).

## Live-Ereignisse

| Typ auf `user:{id}` | Inhalt |
|---|---|
| `chat.message` | `ChatMessageDto`: neu, bearbeitet, gelöscht oder mit Reaktion. Datensätze sind darin nicht aufgelöst (`HasEntityMentions`): lade die Nachricht, um sie mit den Rechten des Lesers zu sehen. |
| `chat.read` | `ChatReadDto` mit `ConversationId` |
| `chat.typing` | `ChatTypingDto` |
| `chat.conversation` | `ChatConversationDto` nach Namens- oder Mitgliederänderungen |

```csharp
await realtime.SubscribeAsync(RealtimeTopics.User(me), envelope =>
{
    if (envelope.Type == ChatEvents.Message) { /* envelope.Payload ist ein ChatMessageDto */ }
});
```

## Aktualisieren

Seit den Vorschaubildern brauchen Apps eine Migration für die Spalte `cw.ChatAttachments.ThumbnailKey`; vorher gesendete Bilder zeigen in der Liste weiter die ganze Datei.

Apps brauchen eine Migration (`cw.ChatConversations`, `cw.ChatMembers`, `cw.ChatReactions`, `cw.ChatAttachments`, neue Spalten von `cw.ChatMessages`). Direktnachrichten von vorher wandern beim nächsten Start in Unterhaltungen (`ChatLegacyMessages`); was der Empfänger gelesen hatte, bleibt gelesen.
