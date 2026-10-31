# Ressourcenkalender

`ResourceCalendar` (Namespace `Coworkee.Client.Blazor.Components.Calendar`) zeigt Zeilen von Ressourcen (Personen, Räume, Maschinen) über Spalten von Tagen, mit einem Balken je Eintrag: Abwesenheiten eines Teams, ein Schichtplan, Buchungen. Wochenenden und markierte Tage (Feiertage) sind hinterlegt, heute ist hervorgehoben, überlappende Einträge stapeln sich in Spuren, und jeder Tag hat eine Vormittags- und eine Nachmittagshälfte, damit halbe Tage genau stimmen.

```razor
<ResourceCalendar Resources="_people" Items="_absences" Marks="_holidays"
                  @bind-From="_from" @bind-Span="_span"
                  OnItemClick="OpenAsync" OnRangeSelected="RequestAsync" />
```

```csharp
_people = [new CalendarResource(personId.ToString(), "Ann Lee", "Vertrieb", UserId: userId, Group: "Vertrieb")];
_absences = [new CalendarItem(personId.ToString(), new(2026, 3, 2), new(2026, 3, 6), "Urlaub", "#22c55e", HalfStart: true, Tentative: !approved)];
_holidays = [new CalendarDayMark(new(2026, 4, 3), "Karfreitag")];
```

- `CalendarResource`: `Group` stellt Zeilen unter eine Überschrift; mit `UserId` und ohne `ResourceTemplate` zeigt die Zeile den Avatar der Person (Bild und Anwesenheit) neben `Title` und `Subtitle`. Eine Zeile wächst mit ihrer Ressourcenzelle; die Spuren der Balken bleiben 28px hoch.
- `CalendarItem`: der erste Wert ist die `ResourceId` seiner Zeile (ein Eintrag hat keine eigene Id). `Start` und `End` zählen mit, `HalfStart`/`HalfEnd` beginnen oder enden den Balken mittags, `Tentative` schraffiert ihn (offene Anträge), `Icon` zeigt ein MudBlazor-Icon im Balken, `Data` trägt ein eigenes Objekt zu `OnItemClick`.
- `CalendarDayMark`: hinterlegt einen Tag in allen Zeilen oder mit `ResourceId` in einer.
- `From` und `Span`: der erste angezeigte Tag (standardmäßig heute; ein Monat beginnt an seinem Ersten, eine Woche am ersten Wochentag der Kultur) und `Week`, `TwoWeeks` oder `Month` (Standard); beide lassen sich in beide Richtungen binden. Die Werkzeugleiste blättert um diese Spanne weiter, *Heute* springt zurück. `ToolbarContent` ergänzt eigene Filter, `ShowToolbar="false"` blendet sie aus.
- `WorkDays`: Tage, die als Arbeitstage gezeichnet werden (standardmäßig Montag bis Freitag). `CalendarResource.WorkDays` ersetzt sie für eine Zeile, so werden die freien Tage von Teilzeitkräften schattiert: `new CalendarResource(id, "Bob Ray", WorkDays: new HashSet<DayOfWeek> { DayOfWeek.Monday, DayOfWeek.Tuesday })`.
- Breite: die Tagesspalten teilen sich die verfügbare Breite (ein Monat passt in etwa 800px); darunter scrollt der Kalender seitlich und blendet am rechten Rand aus.
- Weitere Attribute (`id`, `data-testid`, `aria-label`, ...) landen auf dem Wurzelelement und haben Vorrang vor dessen Standardwerten.
- `ResourceTemplate` zeichnet die Ressourcenzelle einer Zeile selbst, `ResourceHeader` ist der Text über der Ressourcenspalte, `Height` begrenzt die Höhe der Zeilen (Standard `auto`; eine CSS-Länge scrollt innen), `Class` gestaltet das Wurzelelement.
- `OnRangeSelected`: Ziehen über die Tage einer Zeile meldet einen `CalendarRange(Resource, Start, End)`, etwa um einen Antragsdialog für diese Tage zu öffnen. Ohne Handler ist das Ziehen aus; ein Bereich beginnt nie auf einem Eintrag.
