# Befehlspalette

**Strg+K** (**Cmd+K** auf dem Mac) oder der Suchknopf in der App-Leiste öffnet die Befehlspalette: ein Feld für Seiten, Datensätze, Aktionen und den Assistenten. Du brauchst keine Maus: tippen, mit den Pfeiltasten wählen, mit **Enter** öffnen, mit **Esc** schließen. Auf dem Handy öffnet sie sich als Blatt am oberen Rand.

Was sie findet:

- **Seiten** der Navigation, die der Benutzer öffnen darf (dieselben Regeln für Berechtigung, Feature und Organisation wie im Menü).
- **Datensätze** aus der [globalen Suche](../web/global-search.md) des Servers, sobald zwei Buchstaben dastehen: Benutzer, Dateien, Berichte und die Sets, die deine Module mit `AddGlobalSearch` registrieren. Sie erscheinen Gruppe für Gruppe, sobald die Provider sie finden, der gewählte Eintrag bleibt gewählt, und *Alle Ergebnisse* öffnet `/search?q=` mit einem Filter pro Typ.
- **Aktionen**: dunkler oder heller Modus, ein anderes Theme, eine andere Sprache, eine andere aktive Rolle und deine eigenen Befehle wie *Neues Produkt*.
- **Den Assistenten fragen: …** als letzter Eintrag, wenn der [Assistent](../modules/ai.md) eingerichtet ist und der Benutzer chatten darf. Er öffnet das Assistentenfenster mit dem Text im Eingabefeld.

Die Suche ist unscharf: `nwprd` findet *New product*, und die passenden Buchstaben sind hervorgehoben. Solange nichts getippt ist, zeigt die Palette die zuletzt geöffneten Links.

## Eigene Einträge

```csharp
// eine Aktion: ein Link oder beliebiger Code
builder.Services.AddCommandPaletteCommand(new CommandPaletteCommand("New product", Icons.Material.Outlined.Add,
    CatalogPermissions.Products.Create, Href: "/catalog/products?new=1"));
```

`?new=1` auf einer Seite mit `CoworkeeDataTable` öffnet einmal deren Anlegen-Dialog, ein Befehl *Neues ...* braucht also nicht mehr. Datensätze registrierst du auf dem Server neben ihrem OData-Set, siehe [Globale Suche](../web/global-search.md).

Für alles andere, was im Browser lebt, schreibst du einen Provider (Daten des Servers gehören in einen Provider der globalen Suche). Er bekommt den bisher getippten Text und liefert Einträge; die Palette filtert und sortiert sie, außer du setzt `Matched = true`, weil du selbst gesucht hast.

```csharp
public sealed class HelpArticles(IHelpApi help) : ICommandPaletteProvider
{
    public async Task<IEnumerable<CommandPaletteItem>> GetItemsAsync(string query, CancellationToken cancellationToken) =>
        query.Length < 3 ? [] : (await help.SearchAsync(query, cancellationToken))
            .Select(a => new CommandPaletteItem("Help", a.Title) { Icon = Icons.Material.Outlined.HelpOutline, Href = a.Url, Matched = true });
}

builder.Services.AddCommandPaletteProvider<HelpArticles>();
```

`Order` sortiert die Gruppen (Seiten 0, Datensätze 10, Aktionen 20, der Assistent 1000), `Keywords` gibt der Suche weitere Wörter, und `Run` ersetzt den Link durch eigenen Code.
