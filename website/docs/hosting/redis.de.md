# Skalieren mit Redis

Redis ist optional. Ein Dienst mit einem Connection-String `redis` teilt seinen Zustand darüber mit den anderen Instanzen; ohne ihn bleibt alles im Prozess und funktioniert für eine einzelne Instanz genauso.

| Was | Mit Redis | Ohne |
|---|---|---|
| SignalR | Backplane: Ein Ereignis erreicht die Verbindungen jeder Instanz | die Verbindungen dieser Instanz |
| Anwesenheit | ein Benutzer zählt einmal, egal welche Instanzen Verbindungen halten | pro Instanz |
| Caches für Berechtigungen, Einstellungen, Features und Texte | eine Änderung verwirft die Einträge sofort auf jeder Instanz | andere Instanzen merken es innerhalb der Cache-Lebensdauer |
| Sitzungs-Stamps und Sitzungsänderungen | gemeinsamer Stamp-Cache, Pub/Sub an jede Instanz ([Clients und Sitzungen](../security/clients.md)) | eigene Instanz, die anderen innerhalb einer Minute |
| Topics nach Berechtigungsänderungen | jede Instanz prüft ihre Verbindungen erneut ([Realtime](../modules/realtime.md)) | eigene Instanz |
| Anmeldesitzungen der Web-App | Sitzungsspeicher und Schlüsselring des Cookies in Redis: von den Instanzen geteilt, sie überstehen Neustarts | Speicher und lokaler Schlüsselring |
| Verteilte Sperre | `Coworkee:DistributedLock:Provider = Redis` ([Ereignisse und Sperren](../fundamentals/events.md)) | die Datenbank: ein Postgres-Advisory-Lock, ein SQL-Server-Application-Lock, mit SQLite eine Dateisperre |

## Aspire

Der AppHost gibt jedem Dienst, der `Coworkee.Realtime` referenziert (direkt oder über `Coworkee.Identity`), einen Redis-Container, ebenso der Web-App. Ein eigenes Redis oder keines:

```csharp
builder.AddCoworkeeApp("shop", options =>
{
    // Produktion: ein vorhandenes Redis
    options.UseRedis(builder.AddConnectionString("redis"));

    // oder: eine Instanz pro Dienst, kein Container
    options.WithoutRedis();
});
```

`coworkee new --redis false` erzeugt den AppHost mit `WithoutRedis()`. Mit Redis (Standard) rufen die erzeugten Service-Defaults zusätzlich `AddRedisClient("redis")` aus `Aspire.StackExchange.Redis` auf: Health-Check und Traces für die Verbindung, die die Coworkee-Dienste mitbenutzen.

## Wie es funktioniert

- **Caches.** HybridCache hält die Caches mit Tags (Berechtigungen, Einstellungen, Features, Texte, gecachte Abfragen) in jeder Instanz, weil ein Entfernen per Tag eine zweite Ebene, die andere Instanzen lesen, nicht erreicht. Jedes Entfernen per Schlüssel oder Tag geht zusätzlich auf den Kanal `coworkee:cache-invalidation`; die anderen Instanzen verwerfen dieselben Einträge aus ihrem lokalen Cache.
- **Anwesenheit.** Jede Instanz schreibt die Benutzer, für die sie Verbindungen hält, in einen eigenen Hash `coworkee:presence:<instanz>`, der nach 30 Sekunden abläuft, wenn die Instanz ihn nicht erneut schreibt (alle 10 Sekunden). Ein Benutzer, der sich verbindet, wird nur gemeldet, wenn keine andere Instanz ihn online hat; ein Benutzer, der geht, wird nur offline gemeldet, wenn keine andere Instanz noch eine Verbindung hält. Die Benutzer einer abgestürzten Instanz fallen mit ihrem Hash heraus.
- **Web-App.** Die Data-Protection-Schlüssel des Sitzungs-Cookies liegen unter `coworkee:bff:data-protection`, sodass jede Instanz ein Cookie liest, das eine andere geschrieben hat.

Ein Redis, das nicht antwortet, ist nicht fatal: Die Dienste protokollieren eine Warnung und arbeiten mit dem, was sie lokal wissen.

!!! note "Grenzen"
    Die Anwesenheitsabfrage liest den Hash jeder Instanz; für eine Handvoll Instanzen ist das in Ordnung. Benutzer einer abgestürzten Instanz verschwinden aus der Anwesenheitsliste, ein Offline-Ereignis wird für sie aber nicht gesendet.
