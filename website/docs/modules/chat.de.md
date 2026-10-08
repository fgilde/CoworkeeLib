# Chat

`Coworkee.Chat` ergänzt Direktnachrichten zwischen den Personen einer Organisation. Neue Nachrichten erreichen Absender und Empfänger über den [Realtime-Hub](realtime.md) auf ihrem Topic `user:{id}`, jeder offene Tab aktualisiert sich sofort.

![Chat-Seite](../assets/screenshots/chat.png){ .shot }

```csharp
[DependsOn(typeof(CoworkeeChatModule))]
public sealed class MyAppDatabaseModule : CoworkeeModule;
```

- Seite `/chat` für Benutzer mit `Chat.Use`: Personen der Organisation mit letzter Nachricht und Anzahl ungelesener, der Verlauf, Enter sendet.
- Nachrichten gehören zum Mandanten; eine Nachricht an jemanden außerhalb der Organisation wird abgelehnt.
- Der Assistent kann Kontakte und Verläufe lesen und Nachrichten senden, als angemeldeter Benutzer ([KI-Werkzeuge](ai.md)).

| Endpunkt | |
|---|---|
| `GET /api/v1/chat/contacts` | Personen mit letzter Nachricht und Anzahl ungelesener |
| `GET /api/v1/chat/conversations/{userId}?before=` | 50 Nachrichten, neueste zuletzt |
| `POST /api/v1/chat/conversations/{userId}` | sendet `{ "text": "..." }` |
| `POST /api/v1/chat/conversations/{userId}/read` | markiert die Nachrichten der Person als gelesen |

Live-Nachrichten in einer eigenen Komponente:

```csharp
await realtime.SubscribeAsync(RealtimeTopics.User(me), envelope =>
{
    if (envelope.Type == ChatEvents.Message) { /* envelope.Payload ist ein ChatMessageDto */ }
});
```
