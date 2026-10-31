# Kalender-Feeds

`Coworkee.Application.Calendar` schreibt iCalendar-Dateien (RFC 5545): Abwesenheiten, Termine, Fristen, alles, was eine Kalender-App importieren oder abonnieren soll. `Results.Ics(...)` aus `Coworkee.AspNetCore.Http` liefert sie aus.

```csharp
using Coworkee.Application.Calendar;
using Coworkee.AspNetCore.Http;

var calendar = new IcsCalendar { Name = "Team absences", RefreshInterval = TimeSpan.FromHours(1) }.Add(
    IcsEvent.AllDay($"{absence.Id}@my-app", absence.First, absence.Last, $"{name}: vacation") with { Transparent = true },
    new IcsEvent($"{talk.Id}@my-app", IcsTime.InZone(talk.StartsAt, "Europe/Berlin"), "Annual review")
    {
        End = IcsTime.InZone(talk.StartsAt.AddHours(1), "Europe/Berlin"),
        Location = "Room 2",
        Url = new Uri($"{appUrl}/talks/{talk.Id}"),
        Status = IcsStatus.Confirmed,
        Organizer = new IcsPerson(lead.Email, lead.Name),
        Attendees = [new IcsPerson(employee.Email, employee.Name)],
    });

return Results.Ics(calendar);              // text/calendar; charset=utf-8
return Results.Ics(calendar, "talks.ics"); // dasselbe als Download
```

| Teil | Was es schreibt |
|---|---|
| `IcsTime` | `DateOnly` → ganzer Tag (`VALUE=DATE`), `DateTimeOffset` → UTC (`…Z`), `IcsTime.InZone(wallClock, "Europe/Berlin")` (ein `DateTime`) → `TZID=` mit der Ortszeit. Die ersten beiden werden implizit umgewandelt oder kommen aus `IcsTime.Date(day)` und `IcsTime.Utc(instant)`. |
| `IcsEvent` | `Uid` (stabil über Abrufe hinweg, damit Änderungen den Termin ersetzen), `Start`, `Summary`, optional `End` (exklusiv), `Description`, `Location`, `Url`, `Status` (`Tentative`, `Confirmed`, `Cancelled`), `Categories`, `Organizer`, `Attendees`, `Transparent` (blockiert die Zeit nicht), `Stamp`. |
| `IcsEvent.AllDay(uid, first, last, summary)` | Tage von `first` bis `last`, beide eingeschlossen (`DTEND` ist der Tag nach `last`). |
| `IcsCalendar` | `Name` (`X-WR-CALNAME`), `RefreshInterval` (`REFRESH-INTERVAL`, `X-PUBLISHED-TTL`), `ProductId`, `Stamp` (Standard jetzt, wird zu `DTSTAMP`), `ToString()`. |

Text wird maskiert (`\`, `;`, `,` und Zeilenumbrüche), Parameter mit `:;,` werden in Anführungszeichen gesetzt, und Zeilen werden nach 75 Oktetts umbrochen, ohne ein UTF-8-Zeichen zu teilen; lange Texte mit Umlauten oder Emojis bleiben gültig.

!!! note "Zeitzonen"
    `TZID` wird ohne `VTIMEZONE`-Block geschrieben. Google Kalender, Outlook und Apple Kalender kennen IANA-IDs; für strenge Parser UTC (`DateTimeOffset`) verwenden.

## Abonnement-Feeds

Kalender-Apps abonnieren eine Adresse und rufen sie alle paar Stunden ab. Sie schicken kein Cookie und keinen Token-Header, deshalb ist die Adresse selbst der Zugang:

```csharp
app.MapGet("/api/v1/absences/ics/{token}", async (string token, AbsenceFeed feed, HttpContext http, CancellationToken ct) =>
    {
        if (await feed.RenderAsync(token, ct) is not { } calendar)
        {
            return Results.NotFound();
        }

        http.Response.Headers.CacheControl = "private, max-age=900";
        http.Response.Headers["X-Robots-Tag"] = "noindex";
        return Results.Ics(calendar);
    })
    .AllowAnonymous();
```

- **Token:** 32 Zufallsbytes (`RandomNumberGenerator.GetHexString(64)`) pro Benutzer und Feed, gespeichert mit der Benutzer-ID. Ein neuer Token macht die alte Adresse ungültig. Mit `IgnoreQueryFilters()` nachschlagen und als dieser Benutzer rendern (`CurrentUserScope.Begin(new ImpersonatedUser(userId, tenantId))`), damit der Feed genau zeigt, was der Benutzer sehen darf.
- **Unbekannter Token:** `404`, nie `401` (Kalender-Apps würden nach einem Passwort fragen).
- **Caching:** `private` verhindert, dass gemeinsame Caches und Proxys eine geheime Adresse speichern; ein `max-age` von 5 bis 60 Minuten reicht, die Apps aktualisieren nach eigenem Plan (Google alle paar Stunden, sonst wie `RefreshInterval` sagt). Kein `no-store`: Outlook lädt sonst bei jedem Öffnen neu.
- **Links:** neben der `https://`-Adresse `webcal://host/api/v1/absences/ics/{token}` anbieten; die meisten Apps öffnen bei `webcal` den Abonnieren-Dialog.
- **Umfang:** den Zeitraum begrenzen (zum Beispiel zwei Monate zurück, ein Jahr voraus) statt die ganze Historie auszuliefern.
