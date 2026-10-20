# Calendar feeds

`Coworkee.Application.Calendar` writes iCalendar (RFC 5545) files: absences, appointments, deadlines, anything a calendar app should import or subscribe to. `Results.Ics(...)` in `Coworkee.AspNetCore.Http` serves one.

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
return Results.Ics(calendar, "talks.ics"); // the same as a download
```

| Part | What it writes |
|---|---|
| `IcsTime` | `DateOnly` → whole day (`VALUE=DATE`), `DateTimeOffset` → UTC (`…Z`), `IcsTime.InZone(wallClock, "Europe/Berlin")` → `TZID=` with the local time. Both convert implicitly. |
| `IcsEvent` | `Uid` (stable across feeds, so updates replace the event), `Start`, `Summary`, optional `End` (exclusive), `Description`, `Location`, `Url`, `Status`, `Categories`, `Organizer`, `Attendees`, `Transparent` (does not block the time), `Stamp`. |
| `IcsEvent.AllDay(uid, first, last, summary)` | Days from `first` to `last`, both included (`DTEND` is the day after `last`). |
| `IcsCalendar` | `Name` (`X-WR-CALNAME`), `RefreshInterval` (`REFRESH-INTERVAL`, `X-PUBLISHED-TTL`), `ProductId`, `Stamp` (default now, used as `DTSTAMP`), `ToString()`. |

Text is escaped (`\`, `;`, `,` and line breaks), parameters with `:;,` are quoted, and lines are folded after 75 octets without splitting a UTF-8 character, so long German or emoji text stays valid.

!!! note "Time zones"
    `TZID` is written without a `VTIMEZONE` block. Google Calendar, Outlook and Apple Calendar know IANA ids; for strict parsers use UTC (`DateTimeOffset`) instead.

## Subscription feeds

Calendar apps subscribe to an address and fetch it every few hours. They send no cookie and no token header, so the address itself is the credential:

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

- **Token:** 32 random bytes (`RandomNumberGenerator.GetHexString(64)`) per user and feed, stored with the user id. Renewing it revokes the old address. Look it up with `IgnoreQueryFilters()` and render as that user (`CurrentUserScope.Begin(new ImpersonatedUser(userId, tenantId))`), so the feed shows exactly what the user may see.
- **Unknown token:** `404`, never `401` (calendar apps would ask for a password).
- **Caching:** `private` keeps shared caches and proxies from storing a URL that is a secret; `max-age` of 5 to 60 minutes is enough, the apps refresh on their own schedule (Google every few hours, whatever `RefreshInterval` says). Do not add `no-store`: Outlook then refetches on every open.
- **Links:** offer `webcal://host/api/v1/absences/ics/{token}` next to the `https://` address; most apps open the subscribe dialog for `webcal`.
- **Size:** limit the range (for example two months back, a year ahead) instead of the whole history.
