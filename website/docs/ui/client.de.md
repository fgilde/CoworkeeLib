# Client und Layout

`Coworkee.Client.Blazor` ist eine vollständige WebAssembly-Shell: Layout, Navigation, Benutzermenü, Benachrichtigungen, Admin-Seiten für Benutzer, Gruppen, Rollen, Einstellungen, Themes, Mail-Vorlagen, Jobs und Audit-Log. Die App ergänzt ihre Seiten und Menüeinträge.

```csharp title="MyApp.Web.Client/Program.cs"
var builder = WebAssemblyHostBuilder.CreateDefault(args);
var baseAddress = new Uri(builder.HostEnvironment.BaseAddress);
builder.Services.AddCoworkeeClient(baseAddress, options =>
{
    options.AppTitle = "MyApp";
    options.AppDescription = "Clean Architecture starter for Blazor, built on Coworkee modules.";
    options.AboutLinks.Add(new AboutLink("Documentation", "https://fgilde.github.io/CoworkeeLib/"));
});
builder.Services.AddHttpClient<ICatalogApi, CatalogApi>(client => client.BaseAddress = baseAddress);
builder.Services.AddSingleton<INavigationContributor, MyAppNavigation>();
builder.Services.Configure<NavigationMenuOptions>(MyAppNavigation.Order);
var host = builder.Build();
await host.Services.InitializeCoworkeeClientAsync();
await host.RunAsync();
```

```razor title="Routes.razor"
<Router AppAssembly="typeof(Program).Assembly" AdditionalAssemblies="new[] { typeof(CoworkeeClientOptions).Assembly }">
    <Found Context="routeData">
        <AuthorizeRouteView RouteData="routeData" DefaultLayout="typeof(CoworkeeLayout)" />
    </Found>
</Router>
```

![Dashboard des Templates](../assets/screenshots/dashboard.png){ .shot }

## Navigation

```csharp
internal sealed class MyAppNavigation : INavigationContributor
{
    private const string CatalogManagement = "Catalog Management";

    public static void Order(NavigationMenuOptions menu) => menu
        .OrderGroup("Personal", 0)
        .OrderGroup(NavigationGroups.Administration, 2)
        .OrderGroup(CatalogManagement, 3);

    public IEnumerable<CoworkeeNavItem> Items =>
    [
        new("Dashboard", "/dashboard", Icons.Material.Outlined.Dashboard, CatalogPermissions.Dashboards.View, Group: "Personal"),
        new("Products", "/catalog/products", Icons.Material.Outlined.ViewCarousel, CatalogPermissions.Products.View, Group: CatalogManagement),
        new("Brands", "/catalog/brands", Icons.Material.Outlined.Sell, CatalogPermissions.Brands.View, Group: CatalogManagement, Order: 1),
    ];
}
```

| `NavigationMenuOptions` | |
|---|---|
| `OrderGroup(name, order)` | Position einer Gruppe; unbekannte Gruppen kommen zuletzt, nach Namen |
| `Hide(hrefs)` | entfernt Einträge, auch die eingebauten Admin-Seiten |
| `ShowHome`, `HomeTitle` | der Startseiten-Link oben |
| `IconForGroup(name, icon)` | das Symbol eines Gruppenknotens |
| `Place(href, group, title?, order?)` | verschiebt einen Link der Bibliothek in eine andere Gruppe; ein Gruppenpfad wie `Administration/Localization` verschachtelt |

Das Menü ist ein `MudExTreeView`: Gruppen sind Knoten, das Filterfeld hebt Treffer hervor und öffnet ihre Gruppen, und die Ansicht wechselt zwischen Baum, Liste und flacher Liste. Angeheftet bleibt die Leiste als Symbolstreifen unter der App-Leiste, der Menüknopf verbreitert sie; nicht angeheftet öffnet sie sich als Überlagerung und verschwindet wieder. Ob eine oder mehrere Gruppen offen bleiben, ist ein zweiter Schalter. Beides speichert der Browser.

![Eingeklappte Navigation](../assets/screenshots/mini-drawer.png){ .shot }

## App-Leiste

Die App-Leiste zeigt Theme-Menü, Rechts-nach-links-Schalter, Dunkelmodus, Benachrichtigungen, Benutzermenü und den Info-Dialog. Eigene Komponenten kommen so dazu:

```csharp
internal sealed class LanguageBar : IAppBarContributor
{
    public IEnumerable<AppBarItem> Items => [new AppBarItem(typeof(LanguageSwitch), Order: 10)];
}
```

![Info-Dialog](../assets/screenshots/about.png){ .shot }

## Die eigene API aufrufen

Typisierte Clients erben von `ApiClientBase`. Sie senden den CSRF-Header, den der BFF verlangt, und machen aus Problem Details eine `ApiException`:

```csharp
internal sealed class CatalogApi(HttpClient http) : ApiClientBase(http), ICatalogApi
{
    public Task<DashboardDto> GetDashboardAsync(CancellationToken cancellationToken = default) => GetAsync<DashboardDto>("api/v1/dashboard", cancellationToken);

    public Task DeleteBrandsAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Post, "api/v1/brands/delete", new IdsRequest(ids), cancellationToken);
}
```

`Snackbar.RunAsync(() => api.SaveAsync(...), "Saved")` führt einen Aufruf aus und zeigt Erfolg oder die Validierungsmeldungen eines Fehlers.

## Personen

`UserAvatar` zeigt das Bild eines Benutzers oder seine Initialen, `UserCard` Bild und Name mit optionaler zweiter Zeile. Beide laden gebündelt und aktualisieren sich für alle, sobald jemand Name oder Bild auf der Profilseite ändert (Realtime-Topic `tenant:users`).

![Profilseite](../assets/screenshots/profile.png){ .shot }

```razor
<UserCard UserId="entry.ActorId">@entry.CreatedAt.ToString("g")</UserCard>
<UserAvatar UserId="message.FromUserId" Size="Size.Small" ShowTooltip="true" />
```

## Kontoseite

`/profile` enthält die Tabs Profil (Bild, Name, Telefon, Adresse), Sicherheit, Benachrichtigungen und Einstellungen; `/profile/{key}` öffnet einen direkt. Mit einem `ProfileTab` fügen Sie eigene hinzu:

```csharp
builder.Services.AddSingleton(new ProfileTab("Documents", "documents", typeof(MyDocuments), DocumentPermissions.Documents.View));
```

## Bausteine

| Komponente | Zweck |
|---|---|
| `PageHeader` | Titel, Beschreibung, Aktionen rechts; setzt den Browsertitel |
| `PermissionGate`, `PermissionView` | rendern Inhalt nur mit einer Berechtigung |
| `RealtimeSubscription` | ruft zurück, wenn der Server auf einem Thema veröffentlicht, z. B. `type:Brand` |
| `CoworkeeDataTable<T>` | [Datentabellen](data-table.md) |
| `ShowEditAsync` | aus einem Modell erzeugter Bearbeitungsdialog |
| `AuditTimeline`, `VersionHistory` | Änderungshistorie einer Entity |
| `ResourcePermissionsPanel` | Berechtigungen auf einer einzelnen Ressource |
