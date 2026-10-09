# Komponenten ersetzen

Jede Komponente der Shell lässt sich durch eine eigene ersetzen: Ersatz registrieren, und überall, wo das Original gerendert würde, erscheint Ihre. Kein Fork, kein kopiertes Layout.

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

Das Layout ist aufgeteilt, damit Sie einen Teil statt des Ganzen ersetzen:

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

Seiten funktionieren genauso: Ersetzen Sie `Profile` durch eine eigene Profilseite, und jeder Link dorthin öffnet Ihre.

## Info-Dialog

Der Info-Dialog fährt von rechts herein und zeigt die Abschnitte aus `options.About.Sections` der Reihe nach: `AboutHeader` (Logo oder Monogramm, Titel, Version), `AboutCredits` (die Bibliotheken mit Logo und laufender Version), `AboutLinks` (die `AboutLinks` der App) und `AboutFooter` (Runtime und gilde.org-Zeichen). Abschnitte sind normale Komponenten: entfernen, einfügen oder tauschen Sie sie; die Credits sind ebenfalls eine Liste.

```csharp
builder.Services.AddCoworkeeClient(baseAddress, options =>
{
    options.About.Sections.Remove(typeof(AboutCredits));
    options.About.Sections.Insert(1, typeof(LicenseSection));
    options.About.Credits.Add(new AboutCredit("MyLib", "https://example.org", Icons.Material.Outlined.Extension, AboutVersion.Of(typeof(MyLib).Assembly)));
});
```

Für einen ganz anderen Dialog ersetzen Sie ihn: `builder.Services.ReplaceComponent<AboutDialog, MyAbout>();`. `MyAbout` ist ein normaler `MudDialog`.

## Ersetzen oder beitragen

Nehmen Sie die Erweiterungspunkte, wenn sie passen: `INavigationContributor` für Menüeinträge, `IAppBarContributor` für die obere Leiste, `NavigationMenuOptions` zum Ausblenden und Sortieren. Ersetzen Sie eine Komponente, wenn Sie anderes Markup oder Verhalten brauchen.
