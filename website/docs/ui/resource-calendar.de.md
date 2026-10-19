# Ressourcenkalender

`ResourceCalendar` (Namespace `Coworkee.Client.Blazor.Components.Calendar`) zeigt Zeilen von Ressourcen (Personen, Räume, Maschinen) über Spalten von Tagen, mit einem Balken je Eintrag: Abwesenheiten eines Teams, ein Schichtplan, Buchungen. Wochenenden und markierte Tage (Feiertage) sind hinterlegt, heute ist hervorgehoben, überlappende Einträge stapeln sich in Spuren, und jeder Tag hat eine Vormittags- und eine Nachmittagshälfte, damit halbe Tage genau stimmen.

```razor
<ResourceCalendar Resources="_people" Items="_absences" Marks="_holidays"
                  @bind-From="_from" @bind-Span="_span"
                  OnItemClick="OpenAsync" OnRangeSelected="RequestAsync" />
```

```csharp
_people = [new CalendarResource(id.ToString(), "Ann Lee", "Vertrieb", UserId: userId, Group: "Vertrieb")];
_absences = [new CalendarItem(id.ToString(), new(2026, 3, 2), new(2026, 3, 6), "Urlaub", "#22c55e", HalfStart: true, Tentative: !approved)];
_holidays = [new CalendarDayMark(new(2026, 4, 3), "Karfreitag")];
```

- `CalendarResource`: `Group` stellt Zeilen unter eine Überschrift; mit `UserId` und ohne `ResourceTemplate` zeigt die Zeile die Benutzerkarte (Avatar und Anwesenheit).
- `CalendarItem`: `Start` und `End` zählen mit, `HalfStart`/`HalfEnd` beginnen oder enden den Balken mittags, `Tentative` schraffiert ihn (offene Anträge), `Data` trägt ein eigenes Objekt zu `OnItemClick`.
- `CalendarDayMark`: hinterlegt einen Tag in allen Zeilen oder mit `ResourceId` in einer.
- `Span`: `Week`, `TwoWeeks` oder `Month`; die Werkzeugleiste blättert darum weiter, *Heute* springt zurück. `ToolbarContent` ergänzt eigene Filter, `ShowToolbar="false"` blendet sie aus.
- `WorkDays`: Tage, die als Arbeitstage gezeichnet werden (standardmäßig Montag bis Freitag). `CalendarResource.WorkDays` ersetzt sie für eine Zeile, so werden die freien Tage von Teilzeitkräften schattiert: `new CalendarResource(id, "Bob Ray", WorkDays: new HashSet<DayOfWeek> { DayOfWeek.Monday, DayOfWeek.Tuesday })`.
- Weitere Attribute (`id`, `data-testid`, `aria-label`, ...) landen auf dem Wurzelelement und haben Vorrang vor dessen Standardwerten.
- `OnRangeSelected`: Ziehen über die Tage einer Zeile meldet einen `CalendarRange(Resource, Start, End)`, etwa um einen Antragsdialog für diese Tage zu öffnen.
