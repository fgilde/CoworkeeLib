# Resource calendar

`ResourceCalendar` (namespace `Coworkee.Client.Blazor.Components.Calendar`) draws rows of resources (people, rooms, machines) against columns of days, with a bar per item: absences of a team, a shift roster, bookings. Weekends and marked days (public holidays) are shaded, today is highlighted, overlapping items stack in lanes, and every day has a morning and an afternoon half, so half days are exact.

```razor
<ResourceCalendar Resources="_people" Items="_absences" Marks="_holidays"
                  @bind-From="_from" @bind-Span="_span"
                  OnItemClick="OpenAsync" OnRangeSelected="RequestAsync" />
```

```csharp
_people = [new CalendarResource(personId.ToString(), "Ann Lee", "Sales", UserId: userId, Group: "Sales")];
_absences = [new CalendarItem(personId.ToString(), new(2026, 3, 2), new(2026, 3, 6), "Vacation", "#22c55e", HalfStart: true, Tentative: !approved)];
_holidays = [new CalendarDayMark(new(2026, 4, 3), "Good Friday")];
```

- `CalendarResource`: `Group` puts rows under a header; with a `UserId` and no `ResourceTemplate` the row shows the user's avatar (picture and presence) next to `Title` and `Subtitle`. A row grows to its resource cell; the lanes of the bars stay 28px high.
- `CalendarItem`: the first value is the `ResourceId` of its row (an item has no id of its own). `Start` and `End` are inclusive, `HalfStart`/`HalfEnd` start or end the bar at noon, `Tentative` hatches it (pending requests), `Icon` shows a MudBlazor icon in the bar, `Data` carries your own object to `OnItemClick`.
- `CalendarDayMark`: shades a day for every row, or for one row with `ResourceId`.
- `From` and `Span`: the first day shown (default today; a month starts at its first day, a week at the first day of the week of the culture) and `Week`, `TwoWeeks` or `Month` (default); both bind two-way. The toolbar moves by the span and *Today* goes back. `ToolbarContent` adds your filters to the toolbar, `ShowToolbar="false"` hides it.
- `WorkDays`: days drawn as working days (Monday to Friday by default). `CalendarResource.WorkDays` replaces them for one row, so the free days of part-time people are shaded: `new CalendarResource(id, "Bob Ray", WorkDays: new HashSet<DayOfWeek> { DayOfWeek.Monday, DayOfWeek.Tuesday })`.
- Width: the day columns share the available width (a month fits about 800px); below that the calendar scrolls sideways and fades at its right edge.
- Further attributes (`id`, `data-testid`, `aria-label`, ...) land on the root element and win over its defaults.
- `ResourceTemplate` renders the resource cell of a row yourself, `ResourceHeader` is the text above the resource column, `Height` caps the height of the rows (default `auto`; a CSS length scrolls inside), `Class` styles the root element.
- `OnRangeSelected`: dragging over the days of one row reports a `CalendarRange(Resource, Start, End)`, for example to open a request dialog for those days. Without a handler dragging is off; a range never starts on an item.
