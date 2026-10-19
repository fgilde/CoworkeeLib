# Resource calendar

`ResourceCalendar` (namespace `Coworkee.Client.Blazor.Components.Calendar`) draws rows of resources (people, rooms, machines) against columns of days, with a bar per item: absences of a team, a shift roster, bookings. Weekends and marked days (public holidays) are shaded, today is highlighted, overlapping items stack in lanes, and every day has a morning and an afternoon half, so half days are exact.

```razor
<ResourceCalendar Resources="_people" Items="_absences" Marks="_holidays"
                  @bind-From="_from" @bind-Span="_span"
                  OnItemClick="OpenAsync" OnRangeSelected="RequestAsync" />
```

```csharp
_people = [new CalendarResource(id.ToString(), "Ann Lee", "Sales", UserId: userId, Group: "Sales")];
_absences = [new CalendarItem(id.ToString(), new(2026, 3, 2), new(2026, 3, 6), "Vacation", "#22c55e", HalfStart: true, Tentative: !approved)];
_holidays = [new CalendarDayMark(new(2026, 4, 3), "Good Friday")];
```

- `CalendarResource`: `Group` puts rows under a header; with a `UserId` and no `ResourceTemplate` the row shows the user's card (avatar and presence).
- `CalendarItem`: `Start` and `End` are inclusive, `HalfStart`/`HalfEnd` start or end the bar at noon, `Tentative` hatches it (pending requests), `Data` carries your own object to `OnItemClick`.
- `CalendarDayMark`: shades a day for every row, or for one row with `ResourceId`.
- `Span`: `Week`, `TwoWeeks` or `Month`; the toolbar moves by it and *Today* goes back. `ToolbarContent` adds your filters to the toolbar, `ShowToolbar="false"` hides it.
- `WorkDays`: days drawn as working days (Monday to Friday by default).
- `OnRangeSelected`: dragging over the days of one row reports a `CalendarRange(Resource, Start, End)`, for example to open a request dialog for those days.
