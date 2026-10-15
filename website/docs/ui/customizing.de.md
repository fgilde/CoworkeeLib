# Komponenten ersetzen

Jede Komponente der Shell lässt sich durch eine eigene ersetzen: Ersatz registrieren, und überall, wo das Original gerendert würde, erscheint deine. Kein Fork, kein kopiertes Layout.

```csharp
builder.Services.ReplaceComponent<CoworkeeUserMenu, MyUserMenu>();
```

```razor title="MyUserMenu.razor"
<MudMenu Icon="@Icons.Material.Outlined.AccountCircle" Color="Color.Inherit">
    <MudMenuItem Href="/profile">Profile</MudMenuItem>
    <MudMenuItem Href="/help">Help</MudMenuItem>
    <MudMenuItem Href="/bff/logout">Sign out</MudMenuItem>
</MudMenu>
```

Der Ersatz bekommt die Parameter, die das Original bekäme, und muss sie deshalb deklarieren (`[Parameter]`-Eigenschaften mit denselben Namen). Wer das Original behalten und nur wenig ändern will, erbt davon. Ersetzungen, die wieder bei sich selbst landen, werden beim Start abgelehnt.

## Teile des Layouts

Das Layout ist aufgeteilt, damit du einen Teil statt des Ganzen ersetzt:

| Komponente | Teil |
|---|---|
| `CoworkeeLayout` | der gesamte Seitenrahmen |
| `CoworkeeAppBar` | obere Leiste |
| `CoworkeeBrand` | Logo und Titel in der oberen Leiste |
| `CoworkeeNavMenu` | Inhalt der Navigationsleiste |
| `CoworkeeNavLink` | ein einzelner Menüeintrag |
| `CoworkeeUserMenu` | Benutzermenü |
| `NotificationBell` | Benachrichtigungen |
| `ThemeMenu` | Schnellwahl für Themes |
| `AboutDialog` | Info-Dialog |

Seiten funktionieren genauso: Ersetze `Profile` durch eine eigene Profilseite, und jeder Link dorthin öffnet deine.

## Info-Dialog

Der Info-Dialog fährt von rechts herein und zeigt die Abschnitte aus `options.About.Sections` der Reihe nach: `AboutHeader` (Logo oder Monogramm, Titel, Version), `AboutCredits` (die Bibliotheken mit Logo und laufender Version), `AboutLinks` (die `AboutLinks` der App) und `AboutFooter` (Runtime und gilde.org-Zeichen). Abschnitte sind normale Komponenten: du kannst sie entfernen, einfügen oder tauschen; die Credits sind ebenfalls eine Liste.

```csharp
builder.Services.AddCoworkeeClient(baseAddress, options =>
{
    options.About.Sections.Remove(typeof(AboutCredits));
    options.About.Sections.Insert(1, typeof(LicenseSection));
    options.About.Credits.Add(new AboutCredit("MyLib", "https://example.org", Icons.Material.Outlined.Extension, AboutVersion.Of(typeof(MyLib).Assembly)));
});
```

Für einen ganz anderen Dialog ersetzt du ihn: `builder.Services.ReplaceComponent<AboutDialog, MyAbout>();`. `MyAbout` ist ein normaler `MudDialog`.

## Ersetzen oder beitragen

Nimm die Erweiterungspunkte, wenn sie passen: `INavigationContributor` für Menüeinträge, `IAppBarContributor` für die obere Leiste, `NavigationMenuOptions` zum Ausblenden und Sortieren. Ersetze eine Komponente, wenn du anderes Markup oder Verhalten brauchst.
