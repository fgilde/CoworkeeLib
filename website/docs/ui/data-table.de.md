# Datentabellen

`CoworkeeDataTable<T>` ist ein MudBlazor-Datagrid auf einem [OData-Set](../web/odata.md). Der Server blättert, sortiert, filtert und zählt; die Tabelle ergänzt Suche, Facetten, Export, Auswahl und die Schaltflächen zum Anlegen, Bearbeiten und Löschen mit ihren Berechtigungen.

```razor
<CoworkeeDataTable T="ProductDto" EntitySet="Products" Expand="Brand" SearchFields="SearchFields" MultiSelection="true" ExportFileName="products"
                   CreatePermission="@CatalogPermissions.Products.Create" EditPermission="@CatalogPermissions.Products.Edit" DeletePermission="@CatalogPermissions.Products.Delete"
                   OnCreate="CreateAsync" OnEdit="EditAsync" OnDelete="DeleteAsync" DescribeItem="p => p.Name">
    <Columns>
        <PropertyColumn Property="p => p.Name" />
        <PropertyColumn Property="p => p.Brand!.Name" Title="Brand" />
        <PropertyColumn Property="p => p.Rate" Format="0.00" />
    </Columns>
</CoworkeeDataTable>
```

![Produkte mit geöffneter Marken-Facette](../assets/screenshots/products.png){ .shot }

| Parameter | |
|---|---|
| `EntitySet` | Name des OData-Sets |
| `Columns` | MudBlazor-Spalten; verschachtelte Eigenschaften sortieren als `Brand/Name` |
| `Expand` | zu ladende Navigationseigenschaften, z. B. `"Brand"` |
| `Filter` | ein fester OData-Filter zusätzlich zur Auswahl des Benutzers |
| `SearchFields` | Eigenschaften, in denen das Suchfeld sucht (`contains`, ohne Groß-/Kleinschreibung) |
| `Facets` | Facettenmenüs anzeigen (Standard `true`) |
| `MultiSelection` | Checkboxen und eine Schaltfläche *Delete n* |
| `OnCreate`, `OnEdit`, `OnDelete` | Callbacks; die Tabelle lädt danach jeweils neu |
| `CreatePermission`, `EditPermission`, `DeletePermission` | blenden die Schaltflächen ohne Berechtigung aus |
| `DescribeItem` | Text für die Löschbestätigung |
| `Exportable`, `ExportFileName` | CSV- und JSON-Export aller Zeilen des aktuellen Filters |
| `ToolBarContent` | zusätzliche Schaltflächen in der Werkzeugleiste |
| `PageSize` | Zeilen pro Seite (25); beim endlosen Scrollen kommen die Zeilen in Blöcken von mindestens 50 |
| `PageMode` | `FromSettings` (Standard), `Pager` oder `EndlessScroll`, siehe [Seiten oder endloses Scrollen](#seiten-oder-endloses-scrollen) |
| `EndlessHeight` | Höhe der scrollenden Zeilen beim endlosen Scrollen, jede CSS-Länge (`calc(100vh - 300px)`) |
| `Groupable`, `ReorderableColumns`, `ResizableColumns`, `DensityToggle` | Gruppierung, Spalten per Drag & Drop verschieben, Spaltenbreite ziehen und die Dichte-Schaltfläche (alle `true`) |
| `DataSource` | Zeilen aus einer anderen Quelle als dem OData-Set, siehe unten |
| `ColumnFilters` | ein Filter in jedem Spaltenkopf, siehe [Spaltenfilter](#spaltenfilter) (Standard `options.DataTable.ColumnFilters`, `true`) |
| `FacetLayout` | `Bar` (Pillen über den Zeilen, Standard) oder `Panel` (eine Seitenleiste mit allen Facetten offen), siehe [Facetten](#facetten) |
| `FacetSettings` | das letzte Wort der Seite je Facette: Ansicht, *eines von / alle von*, Ausschluss, Ausblenden |
| `History`, `ItemId` | eine Verlaufsaktion je Zeile und ein Knopf *Gelöschte Datensätze*, siehe [Verlauf eines Datensatzes](../modules/auditing.md#verlauf-eines-datensatzes-und-wiederherstellen); `ItemId` wählt die Id (sonst die Eigenschaft `Id`) |
| `AdvancedFilter` | die Schaltfläche *Erweiterter Filter* mit dem [Query-Builder](query-builder.md) (Standard `options.DataTable.AdvancedFilter`, `true`) |
| `QueryFields` | die Felder des Query-Builders; standardmäßig die Eigenschaftsspalten und die Referenz-Facetten |

Die Tabelle lädt von selbst neu, wenn ihre `[Realtime]`-Entity sich ändert (`RealtimeTopic`, siehe [Realtime](../modules/realtime.md#anderungen-an-entities)). `ReloadAsync()` lädt von außen neu, etwa aus einer `RealtimeSubscription`. `Selection` liefert die Facettenauswahl, `CurrentFilter` den OData-Filter, den die Tabelle sendet (fester Filter, Facetten, Spaltenfilter und Suche).

Für Sets, die als [Client-Entities](client-entities.md) markiert sind, filtert, sucht, sortiert und zählt die Tabelle im Browser, sobald die Zeilen angekommen sind; ein Schalter in ihrer Werkzeugleiste zeigt, wo gesucht wird, und lässt die Wahl.

## Seiten oder endloses Scrollen

Mit `PageMode="FromSettings"` (Standard) folgt eine Tabelle der Einstellung **Tabellenzeilen** (`DataTable.DefaultPageMode`): `Pager` (Standard) oder `EndlessScroll`. Administratoren legen sie unter *Einstellungen → System* für alle fest, jeder Benutzer kann sie unter *Meine Einstellungen* überschreiben; offene Tabellen wechseln, sobald gespeichert ist. `PageMode="Pager"` oder `PageMode="EndlessScroll"` legt es für eine Tabelle fest.

Endloses Scrollen zeigt eine Liste mit festem Kopf (`EndlessHeight`) und der Gesamtzahl darunter. Gerendert werden nur die sichtbaren Zeilen; die Zeilen kommen beim Scrollen in Blöcken von `PageSize` (mindestens 50) und bleiben, bis sich die Abfrage ändert, Zurückscrollen braucht also keine Anfrage. Nur der erste Block einer Abfrage fragt nach Anzahl und Facetten. Sortieren, Suche, Facetten, Spaltenfilter, Auswahl, Realtime-Neuladen, Export (immer das ganze gefilterte Ergebnis) und Client-Entities funktionieren wie mit Seiten.

MudBlazor kann gruppierte Zeilen nicht virtualisieren: Solange die Tabelle gruppiert ist, zeigt sie Seiten mit dem Hinweis *Gruppierte Zeilen werden seitenweise angezeigt.* und scrollt wieder, sobald die Gruppierung entfernt ist.

```razor
<CoworkeeDataTable T="SalesOrderDto" EntitySet="SalesOrders" PageMode="DataTablePageMode.EndlessScroll" EndlessHeight="70vh" ...>
```

## Einträge der Werkzeugleiste

Ein Paket kann eine Komponente in die Werkzeugleiste jeder Tabelle über einem OData-Set legen: `new DataTableToolbarItem(typeof(MyComponent))` in DI registrieren. Die Komponente bekommt die Tabelle als Parameter `Table` (`IDataTable`: `EntitySet`, `Expand`, `CurrentQuery`, `ReloadAsync()`). Der [Client-Entity-Schalter](client-entities.md#der-schalter-in-der-tabelle) kommt so in die Tabelle.

Der CSV-Export entschärft Werte, die Tabellenkalkulationen sonst als Formel ausführen würden.

## Facetten

Facetten kommen vom Server (`[ProvideFacet]` an der Entität, siehe [OData und Facetten](../web/odata.md#facetten)) mit ihren Werten, Anzahlen und dem OData-Filter jedes Werts. Die Tabelle zeigt jede Facette in einer Ansicht, die zu ihren Werten passt:

| Ansicht | für | Standard für |
|---|---|---|
| `Checkbox` | eine Liste mit Anzahlen, einer Suche über langen Listen und *mehr anzeigen* | Text und Referenzen |
| `Chips` | Chips mit Anzahlen | Enums und Flags mit bis zu 8 Werten |
| `Select` | eine Auswahlliste, kompakt für lange Listen | |
| `Buttons` | eine Reihe von Schaltflächen, bei Einfachauswahl mit *Alle* vorne | Radio-Facetten mit bis zu 4 Werten |
| `Swatches` | Farbpunkte, die Farbe aus `[FacetColor]`, einem Meta-Wert `color` oder dem Wert selbst | |
| `Tree` | Werte an einem Trenner aufgeteilt ("image/png" unter "image") | |
| `Toggle` | alle, ja oder nein | `bool` |
| `Range` | ein Histogramm über einem Bereichsregler mit zwei Griffen (`MudExRangeSlider`, Pfeiltasten, Bild auf/ab, Pos1 und Ende bewegen einen Griff) mit Feldern *von* und *bis*; Schieben filtert, sobald die Griffe ruhen | Zahlen mit `FacetType.Range` |
| `Dates` | Vorgaben (heute, diese Woche, letzte 30 Tage, ...), ein Histogramm nach Tagen, Monaten oder Jahren und ein eigener Zeitraum, eingetippt oder zwischen dem ersten und letzten Tag geschoben | Datumswerte |

![Aufträge mit der Facetten-Leiste](../assets/screenshots/facets-panel.png){ .shot }

Enum-Werte zeigen ihr `[Display(Name)]`, sonst ihre `[Description]`, sonst den Membernamen in Worten (`InTransit` wird zu *In transit*), übersetzt wie jeder andere Text. Adresse, gespeicherte Ansichten und der OData-Filter behalten den Membernamen, eine neue Beschriftung bricht also keinen Link.

### Wie Werte verknüpft werden

| `FacetCombine` | Bedeutung | OData |
|---|---|---|
| `Any` (Standard) | Zeilen mit einem der gewählten Werte | `(Status eq 'Open' or Status eq 'Shipped')` |
| `All` | Zeilen mit allen gewählten Werten, für Werte, die zugleich gelten können, etwa `[Flags]`-Enums | `(Options has 'Express' and Options has 'Insured')` |
| `Single` | ein Wert auf einmal | `Color eq 'Red'` |

Mit `AllowExclude` lässt sich ein Wert auch ausschließen: die Schaltfläche &#8856; in Listen, ein Rechtsklick auf Chips. Ausgeschlossene Werte werden mit `not` verknüpft: `(Status eq 'Open' and not (Status eq 'Cancelled'))`. Gruppen verknüpfen sich immer mit `and`.

Die Anzahlen folgen den anderen Auswahlen, lassen aber die eigene Auswahl einer Gruppe weg, wie in Shop-Filtern: nach *Offen* zeigt *Unterwegs* weiter, wie viele Zeilen es hinzufügen würde. Die Tabelle schickt die Auswahl getrennt vom übrigen Filter im Header `Coworkee-Facets`; [Client-Entities](client-entities.md) zählen im Browser genauso. Gruppen mit *alle von* zählen mit ihrer eigenen Auswahl, weil jeder weitere Wert einschränkt.

### Eine Facette einstellen

Auf dem Server, neben `[ProvideFacet]`:

```csharp
[ProvideFacet(Label = "Status")]
[FacetBehavior(View = FacetView.Chips, AllowExclude = true, Switchable = true)]
public SalesOrderStatus Status { get; set; }

[ProvideFacet(Label = "Options")]
[FacetBehavior(Combine = FacetCombine.All, Switchable = true)]
public SalesOrderOptions Options { get; set; }   // [Flags]-Enum: jedes Flag ist ein Wert

[ProvideFacet(Label = "Total", Type = FacetType.Range)]
[FacetBehavior(Buckets = 16)]
public decimal Total { get; set; }
```

oder ohne die Entität anzufassen:

```csharp
services.ConfigureFacet<SalesOrder>(o => o.Region, f => { f.View = FacetView.Select; f.AllowExclude = true; });
```

Mit `Switchable` wechseln Benutzer zwischen *eines von* und *alle von* (wo Werte zugleich gelten können) und zwischen den passenden Ansichten (das Regler-Menü einer Facette); ihre Wahl gehört zu gespeicherten Ansichten. Ohne Angabe bedeutet `MultiSelect = false` an `[ProvideFacet]` `Single` und `GroupOperator = And` `All`.

Die Seite hat das letzte Wort:

```razor
<CoworkeeDataTable T="SalesOrderDto" EntitySet="SalesOrders" FacetLayout="FacetLayout.Panel" FacetSettings="FacetSettings" ...>

@code {
    private static readonly Dictionary<string, FacetSettings> FacetSettings = new()
    {
        ["region"] = new() { View = FacetView.Select, AllowExclude = true },
        ["customer"] = new() { Hidden = true },
    };
}
```

Schlüssel sind die Facetten-Schlüssel: der Eigenschaftsname in camelCase (`orderDate`).

**Bereiche**: Zahlen-Facetten mit `FacetType.Range` und Datums-Facetten bekommen ihren kleinsten und größten Wert und ein Histogramm, dessen Balken Optionen mit eigenem Filter sind; Client-Entities zählen sie so lokal nach. Der gewählte Bereich ist ein Wert, `(Total ge 40 and Total le 100)`. Datumsvorgaben bleiben relativ: eine gespeicherte Ansicht mit *letzte 7 Tage* meint die letzten 7 Tage, wann immer sie öffnet.

### Facetten anderswo

Die Facetten-Komponenten funktionieren auch ohne Tabelle, etwa neben einem Diagramm oder in einem Bericht:

```razor
<FacetPanel Groups="_groups" Selection="_selection" />           @* oder <FacetBar ... /> *@

@code {
    private readonly FacetSelection _selection = new();
    private IReadOnlyList<FacetGroupDto> _groups = [];

    protected override async Task OnInitializedAsync()
    {
        _selection.Changed += () => InvokeAsync(LoadAsync);
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        var page = await OData.QueryAsync<OrderDto>("SalesOrders", new ODataQuery
        {
            Filter = _selection.ToFilter(), Top = 0, Facets = true, FacetContext = _selection.Context(null, _groups),
        });
        _groups = page.Facets;
        StateHasChanged();
    }
}
```

`FacetGroupView` zeigt eine Gruppe (`FacetStyles` einmal rendern), `FacetSelection` hält die Auswahl und schreibt den OData-Filter (`ToFilter()`, `GroupFilter(key)`), `Facets.View(group)` und `Facets.Combine(group)` sagen, was eine Gruppe zeigt und wie sie verknüpft.

## Spaltenfilter

Wie in QuickGrid lässt sich jede Spalte filtern: das Filtersymbol im Spaltenkopf (oder *Filter* im Spaltenmenü) öffnet einen kleinen Editor passend zum Typ der Spalte. Ein ausgefülltes Symbol zeigt, dass die Spalte gefiltert ist, und jeder Filter erscheint als Chip über der Tabelle; sein &times; entfernt ihn, *Alle entfernen* entfernt Filter und Facetten zusammen.

| Spaltentyp | Editor | OData |
|---|---|---|
| Text | *enthält*, *beginnt mit*, *ist gleich* und ein Wert | `contains(tolower(Name),'dri')`, `startswith(...)`, `Name eq 'Drill'` |
| Zahlen | *=*, *>*, *<*, *zwischen* | `Rate gt 3`, `Rate ge 3 and Rate le 10` |
| Datum | *ist*, *nach*, *vor*, *zwischen* (ganze Tage) | `Added ge 2026-02-01T00:00:00+01:00 and Added lt ...` |
| Enums, Ja/Nein | Checkboxen, mehrere sind Alternativen | `(Kind eq 'Hand' or Kind eq 'Power')` |
| eine Spalte mit Facette | die Optionen der Facette mit ihren Anzahlen | der Filter der Facette |

**Facetten und Spaltenfilter sind dasselbe.** Hat das Set eine Facette auf der Eigenschaft der Spalte, zeigt der Spaltenfilter deren Optionen und Anzahlen; ein Haken dort oder im Facettenmenü ist dieselbe Auswahl (derselbe Chip, dieselbe URL). Eine Spalte auf einer Navigationseigenschaft nutzt die Facette ihres Schlüssels: `Brand.Name` nutzt eine Facette auf `BrandId`.

Die Filter werden mit `and` an den OData-Filter der Tabelle gehängt. Der Server beantwortet sie, bei [Client-Entities](client-entities.md) auch der Browser: sie nutzen nur, was beide verstehen (`eq`, `ne`, `gt`, `ge`, `lt`, `le`, `and`, `or`, `not`, `contains`, `startswith`, `endswith`, `tolower`). Sie gehören zum Zustand der Tabelle: gespeicherte Ansichten, geteilte Links und die Adresszeile behalten sie.

### Ab- und einschalten

Spaltenfilter sind standardmäßig an. Jede Ebene kann das ändern:

```razor
@* die ganze Tabelle *@
<CoworkeeDataTable T="ProductDto" EntitySet="Products" ColumnFilters="false" ...>
    <Columns>
        @* nur diese Spalte, obwohl die Tabelle sie abschaltet *@
        <PropertyColumn Property="p => p.Name" Filterable="true" />
        @* diese Spalte nie *@
        <PropertyColumn Property="p => p.Description" Filterable="false" />
        @* nur bestimmte Operatoren *@
        <PropertyColumn Property="p => p.Barcode" FilterOperators="@(new() { FilterOperator.String.Equal })" />
    </Columns>
</CoworkeeDataTable>
```

Template-Spalten haben keine Eigenschaft und sind nicht filterbar. Für die ganze App, je Typ:

```csharp
builder.Services.AddCoworkeeClient(new Uri(builder.HostEnvironment.BaseAddress), options =>
{
    options.DataTable.ColumnFilters = true;   // Standard jeder Tabelle
    options.DataTable.FilterOperators<DateTime>(FilterOperator.DateTime.After, FilterOperator.DateTime.Before, DataTableFilters.Between);
    options.DataTable.FilterOperators<Guid>(); // keine Operatoren: Guid-Spalten sind nicht filterbar
});
```

Die Reihenfolge: `Filterable`/`FilterOperators` der Spalte, dann die Optionen für den Typ, dann die Standards oben.

### Eigener Editor

`FilterTemplate` an einer Spalte ersetzt den Editor. Er bekommt MudBlazors `FilterContext`; Filterdefinitionen mit der Spalte und einem von MudBlazors `FilterOperator`s macht die Tabelle zu OData:

```razor
<PropertyColumn Property="p => p.Rate" Title="Rate">
    <FilterTemplate>
        <MudButton OnClick="@(() => context.Actions.ApplyFilterAsync(new FilterDefinition<ProductDto>
            { Id = Guid.NewGuid(), Column = context.FilterDefinition!.Column, Operator = FilterOperator.Number.GreaterThanOrEqual, Value = 4m }))">
            Ab Rate 4
        </MudButton>
    </FilterTemplate>
</PropertyColumn>
```

`DataTableFilters.ToOData(...)` macht aus solchen Bedingungen selbst OData-Text, etwa für eine `ITableDataSource`.

**Grenzen**: ein Editor pro Spalte (zwei Filter für *zwischen*); der Filter gilt der Eigenschaft der Spalte, nicht dem, was ein `CellTemplate` anzeigt. Datumswerte werden nach ganzen Tagen in der Zeitzone des Browsers gefiltert.

## Erweiterter Filter

*Erweiterter Filter* öffnet den [Query-Builder](query-builder.md) über den Zeilen: verschachtelte Gruppen *alle / eine* aus Bedingungen auf jeder Eigenschaftsspalte, mit Operatoren und Werteeditoren passend zum Typ (Enum-Werte als Auswahl, Datumswerte mit *heute*, *diese Woche* oder *in den letzten n Tagen*, Listen für *ist eines von*). Die Tabelle hängt sein OData an die übrigen Filter, zeigt es als Chip und hält es in Adresse, gespeicherten Ansichten und geteilten Links.

![Erweiterter Filter](../assets/screenshots/advanced-filter.png){ .shot }

Die Felder sind die Eigenschaftsspalten und jede Referenz-Facette, die keine Spalte ist (z. B. *Kunde* als Auswahl seiner Facettenwerte). `QueryFields` ersetzt sie, etwa um eine Suche in einem referenzierten Set hinzuzufügen:

```razor
<CoworkeeDataTable T="SalesOrderDto" EntitySet="SalesOrders" QueryFields="Fields" ...>

@code {
    private IReadOnlyList<QueryField> Fields => [
        new("Number", L["Number"], QueryFieldType.Text),
        new("Total", L["Total"], QueryFieldType.Number),
        new("CustomerId", L["Customer"], QueryFieldType.Guid) { Lookup = SearchCustomersAsync },
    ];
}
```

`Advanced` gibt die Abfrage heraus (eine `QueryGroup`); `options.DataTable.AdvancedFilter = false` blendet die Schaltfläche in allen Tabellen aus.

## Bearbeitungsdialoge

Für einfache Modelle reicht ein erzeugter Dialog:

```csharp
if (await Dialogs.ShowEditAsync("Edit brand", model) is { } saved)
{
    await Snackbar.RunAsync(() => Api.SaveBrandAsync(id, saved), "Brand saved");
}
```

Bearbeitungsdialoge sind Seitenblätter in voller Höhe, die von der Seite hereingleiten, auf die der Benutzer geklickt hat, auf breiten Bildschirmen zweispaltig. Mit `meta => ...` konfigurierst du das `MudExObjectEditForm` (Beschriftungen, Reihenfolge, Editoren). Mit einer Speicherfunktion bleibt der Dialog offen und zeigt die Meldungen der API, wenn das Speichern scheitert:

![Bearbeitungsdialog](../assets/screenshots/edit-sheet.png){ .shot }

```csharp
await Dialogs.ShowEditAsync(L["New product"], model, saved => Api.SaveProductAsync(id, saved), meta =>
{
    meta.Property(p => p.BrandId).WithLabel(L["Brand"])
        .RenderWith<ODataPicker, Guid>(p => p.Value)
        .WithAdditionalAttribute(nameof(ODataPicker.EntitySet), "Brands");
    meta.Property(p => p.ImageDataUrl).WithLabel(L["Image"]).RenderWith<ImageDataUrlEdit, string?>(p => p.Value);
});
```

| Editor | für |
|---|---|
| `ODataPicker` | einen Fremdschlüssel: sucht in einem OData-Entity-Set über eine Texteigenschaft |
| `OptionalODataPicker` | einen optionalen Fremdschlüssel (`Guid?`): wie `ODataPicker`, Leeren ergibt null |
| `ImageDataUrlEdit` | ein Bild, gespeichert als Data-URL |

### Chip-Listen

In einer `ObjectEdit`-Meta rendert `ChipListMeta.RenderAsChips` eine `IReadOnlyList<string>`-Eigenschaft als Chips über die volle Breite des Formulars, mit MudEx' `MudExTagField<string>`: Enter, Leerzeichen, Komma oder Zeilenumbruch, Einfügen (eine eingefügte Liste wird in Chips zerlegt) oder das Verlassen des Felds fügen sie hinzu; das &times; eines Chips entfernt ihn. Die Aktion konfiguriert das Tag-Feld, z. B. `SearchFunc` (Vorschläge), `ValidateChip` (Fehlertext eines ungültigen Werts, `null` bei einem gültigen) und `IsChipClosable` (Chips, die sich nicht entfernen lassen):

```csharp
meta.Property(f => f.Scopes).RenderAsChips(e =>
{
    e.Label = L["Scopes"];
    e.SearchFunc = (text, _) => Task.FromResult(known.Where(s => s.Contains(text, StringComparison.OrdinalIgnoreCase)));
    e.ValidateChip = s => ScopePattern().IsMatch(s) ? null : L["Letters, digits and . _ : - only"];
    e.IsChipClosable = s => s != "openid";
});
```

Die Eigenschaft muss eine `IReadOnlyList<string>` sein. Die Konfigurationsaktion setzt Komponentenparameter direkt, der Aufruf steht deshalb zwischen `#pragma warning disable BL0005` und `restore`.

`ShowSideSheetAsync<TDialog>` öffnet eine eigene Dialogkomponente genauso, `ConfirmAsync` fragt vor etwas, das sich nicht rückgängig machen lässt.

## Spalten, Gruppierung und Ansichten

Alles, was der Benutzer einrichtet, gehört zum Zustand der Tabelle und lässt sich speichern, teilen und zurücksetzen.

- **Spalten**: das Spalten-Menü blendet Spalten ein und aus (mit *Alle*, *Keine* und Suche) und sortiert sie per Drag & Drop um. Auch die Spaltenköpfe lassen sich an eine andere Stelle ziehen, ihr rechter Rand ändert die Breite.
- **Gruppierung**: einen Spaltenkopf auf die Zone über den Köpfen ziehen, *Gruppieren* im Menü der Spalte wählen oder Spalten im Gruppier-Menü ankreuzen. Mehrere Spalten gruppieren verschachtelt, in der Reihenfolge der Chips; das &times; eines Chips entfernt seine Ebene. Gruppenzeilen zeigen den Wert und die Anzahl der Zeilen, klappen einzeln oder alle auf und zu und zeigen unter jeder Gruppe die Aggregate der Spalten (`AggregateDefinition`, z. B. einen Durchschnitt).
- **Sortierung**: mehrere Spaltenköpfe anklicken sortiert nach mehreren Spalten.
- **Dichte**: die Dichte-Schaltfläche wechselt zwischen kompakten und großzügigen Zeilen; ohne Wahl entscheidet das Theme.
- **Ansichten**: das Lesezeichen-Menü speichert den aktuellen Zustand unter einem Namen: Suche, Facetten (mit den von Benutzern gewählten Ansichten), Spaltenfilter, den erweiterten Filter, Reihenfolge, Sichtbarkeit und Breite der Spalten, Gruppierung, Sortierung, Seitengröße und Dichte. Ein Klick stellt ihn wieder her, die Speichern-Schaltfläche überschreibt ihn mit dem aktuellen Zustand, der Stern macht ihn zum Standard, der mit der Tabelle öffnet. *Zurücksetzen* kehrt zu den deklarierten Spalten ohne Filter zurück.
- **Teilen**: die Link-Schaltfläche kopiert eine Adresse, die die Tabelle genau so öffnet, samt Layout. Die Adresszeile selbst hält Suche, Facetten, Spaltenfilter und den erweiterten Filter (`?products=...`), eine gefilterte Liste lässt sich also neu laden und als Lesezeichen speichern. `UrlState="false"` schaltet das ab, `StateKey` benennt den Parameter, wenn eine Seite zwei Tabellen desselben Sets zeigt.

```razor
<CoworkeeDataTable T="ProductDto" EntitySet="Products" Expand="Brand" ...>
    <Columns>
        <PropertyColumn Property="p => p.Name" />
        <PropertyColumn Property="p => p.Brand!.Name" Title="Brand" />
        <PropertyColumn Property="p => p.Rate" Format="0.00" AggregateDefinition="AverageRate" />
    </Columns>
</CoworkeeDataTable>

@code {
    private static readonly AggregateDefinition<ProductDto> AverageRate = new() { Type = AggregateType.Avg, DisplayFormat = "Ø {value}", NumberFormat = "0.00" };
}
```

Spalten heißen nach ihrer Eigenschaft (`Brand.Name`), Template-Spalten nach ihrem `Title`; Spalten ohne beides (Auswahl, Aktionen) bleiben, wo sie sind. Template-Spalten gruppieren nur mit `Groupable="true"` und einem `GroupBy`.

**Gruppierung und Seiten**: der Server blättert, eine Seite gruppiert also die Zeilen, die sie enthält. Die Tabelle sortiert zuerst nach den Gruppierspalten, damit eine Gruppe auf der nächsten Seite weitergeht statt verstreut zu werden; Anzahl und Aggregate einer Gruppe beziehen sich auf die geladene Seite.

**Wo Ansichten liegen**: in der Benutzereinstellung `Table.Views` des [Einstellungsmoduls](../modules/settings.md), sie folgen dem Benutzer also auf jedes Gerät. Ohne angemeldeten Benutzer oder ohne Einstellungsmodul bleiben sie im Local Storage des Browsers. Wer sie woanders ablegen will, ersetzt `ITableViewStore` in DI:

```csharp
builder.Services.AddScoped<ITableViewStore, MyTableViews>();
```

`CurrentState` liefert den Zustand als `DataTableState` (nur, was vom deklarierten Layout abweicht), zum Beispiel für eigene Links; `DataTableState.Encode()` ergibt den Wert für den Query-Parameter.

## Andere Datenquellen

Standardmäßig fragt die Tabelle `EntitySet` über OData ab. `DataSource` nimmt stattdessen eine beliebige `ITableDataSource<T>`, zum Beispiel Zeilen, die im Browser liegen:

```csharp
public sealed class LocalProducts(ProductStore store) : ITableDataSource<ProductDto>
{
    public Task<ODataPage<ProductDto>> QueryAsync(ODataQuery query, CancellationToken cancellationToken = default) =>
        Task.FromResult(store.Query(query));
}
```

Die Abfrage trägt den OData-Text, den die Tabelle baut (Filter aus Suche und Facetten, Sortierung, `Top`/`Skip`), die Quelle beantwortet also diese Teilmenge. `EntitySet` benennt weiterhin Ansichten und URL-Parameter. CSV- und JSON-Export nutzen die Quelle; Excel-Export und -Import bleiben bei OData-Sets.
