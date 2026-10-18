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

## Seiten der Bibliothek

Die Admin-Seiten der Bibliothek liegen unter einem eigenen Präfix: `/cw/admin/users`, `/cw/admin/settings` und so weiter. `/admin` bleibt frei für deine eigenen Admin-Seiten, und ein Update der Bibliothek kann dir keine Adresse wegnehmen, die deine App schon nutzt. Die übrigen Seiten (`/files`, `/chat`, `/people/{id}`, `/profile`, `/settings`, `/notifications`, `/assistant`, `/workflows/approvals`, `/reports`) behalten ihre kurzen Adressen.

```csharp
builder.Services.AddCoworkeeClient(baseAddress, options =>
{
    options.PagePrefix = "/cw";                        // Standard; "" behält die alten /admin/...-Adressen
    options.Pages.Replace<MyUsers>("users")              // deine Komponente unter der Adresse der Bibliothek, mit denselben Parametern
        .Replace("user", "/team/{Id}")                // Links und alte Adressen führen zu deiner eigenen Seite
        .Disable("backups");                            // Menüeintrag und Adresse sind weg
});
```

Alte Adressen funktionieren weiter: `/admin/users/…` in einem Lesezeichen, einer Mail oder einer Push-Benachrichtigung leitet auf die aktuelle Adresse weiter. Menü, Befehlspalette und die Links zwischen den Seiten fragen dieselbe Registrierung (`CoworkeePages.Href(path)`), eine ersetzte oder abgeschaltete Seite ist also überall ersetzt oder weg. Links vom Server (Benachrichtigungen, Mails) nutzen die alte Adresse, der Client löst sie auf.

So funktioniert es: eine Sammelseite der Bibliothek (`CoworkeePageHost`, Route `/{*Path}`) zeigt die Seiten der Bibliothek. Seiten deiner App gewinnen immer, eine eigene `/admin/ai`-Seite stößt also nie mit der Bibliothek zusammen. Eine Adresse, die niemand kennt, zeigt die `NotFoundPage` des Routers, wenn du eine gesetzt hast, sonst ein kurzes „Nicht gefunden.“ im Layout.

Schlüssel: `users`, `user`, `groups`, `roles`, `role`, `clients`, `scopes`, `api-keys`, `settings`, `ai-settings`, `ai-tool-calls`, `audit`, `backups`, `data-transfer`, `editions`, `languages`, `translations`, `mail-settings`, `mail-templates`, `mail-template`, `mail-campaigns`, `mail-log`, `retention`, `services`, `system-status`, `tenants`, `themes`, `webhooks`, `approvals`, `assistant`, `chat`, `person`, `files`, `notifications`, `profile`, `search`, `user-settings`, mit Reporting dazu `reports`, `report`, `report-data`. Ein unbekannter Schlüssel hält die App beim Start an.

## Ersetzen oder beitragen

Nimm die Erweiterungspunkte, wenn sie passen: `INavigationContributor` für Menüeinträge, `IAppBarContributor` für die obere Leiste, `NavigationMenuOptions` zum Ausblenden und Sortieren. Ersetze eine Komponente, wenn du anderes Markup oder Verhalten brauchst.
