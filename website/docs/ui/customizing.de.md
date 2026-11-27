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
| `CoworkeeAppShell` | Start und Provider ohne den Rahmen, siehe [Ein eigenes Layout](#ein-eigenes-layout) |
| `CoworkeeAppBar` | obere Leiste |
| `CoworkeeBrand` | Logo und Titel in der oberen Leiste |
| `CoworkeeNavMenu` | Inhalt der Navigationsleiste |
| `CoworkeeNavLink` | ein einzelner Menüeintrag |
| `CoworkeeUserMenu` | Benutzermenü |
| `NotificationBell` | Benachrichtigungen |
| `ThemeMenu` | Schnellwahl für Themes mit Farbmustern und Farbmodus |
| `LanguageMenu` | Sprachwahl mit Flaggen |
| `AboutDialog` | Info-Dialog |

Seiten funktionieren genauso: Ersetze `Profile` durch eine eigene Profilseite, und jeder Link dorthin öffnet deine.

## Ein eigenes Layout

Für eine App, die gar nicht wie die Admin-App aussehen soll (etwa ein Kundenportal neben der internen App), reicht das Ersetzen von Teilen nicht: Schreib ein eigenes Layout und leg `CoworkeeAppShell` darum. Die Shell erledigt alles, was `CoworkeeLayout` außer dem Rahmen tut: Setup-Prüfung, Weiterleitung zur Anmeldung (`AllowAnonymous`), Benutzereinstellungen, Theme, Sprache, Head-Skript, die MudBlazor-Provider (Theme, Dialoge, Popover, Snackbars), den Sitzungswächter (Übernahme beendet, anderswo abgemeldet), Live-Einstellungen und das Ausblenden des Ladebildschirms. `CoworkeeLayout` ist der Admin-Rahmen in derselben Shell.

```razor title="AppLayout.razor"
@inherits LayoutComponentBase
<CoworkeeAppShell Theme="PortalTheme.Value" DarkMode="false">
    <div class="portal">
        <PortalSidebar />
        <main>@Body</main>
    </div>
</CoworkeeAppShell>
```

| Parameter | |
|---|---|
| `ChildContent` | dein Layout; es bekommt die Shell als `context` (`context.Dark`, `context.ToggleDarkAsync()`) |
| `Theme` | ein festes `MudTheme` statt des Coworkee-Themes des Mandanten (Administration, Themes); dessen eigenes CSS und Logo (Favicon) gelten dann nicht |
| `DarkMode` | `true`/`false` legt den Farbmodus fest, `null` (Standard) folgt der Wahl des Benutzers oder dem System |

Mehrere Layouts (etwa ein Haupt- und ein Admin-Layout) teilen sich eine Shell, wenn sie dasselbe äußere Layout nennen (`@layout AppLayout`): Beim Wechsel bleiben Theme, Provider und der gestartete Zustand erhalten. Kombiniere es mit `Pages.Only()` (unten), damit in so einer App keine Seite der Bibliothek erreichbar ist.

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

Die Admin-Seiten der Bibliothek liegen unter einem eigenen Präfix: `/cw/admin/users`, `/cw/admin/settings` und so weiter. `/admin` bleibt frei für deine eigenen Admin-Seiten, und ein Update der Bibliothek kann dir keine Adresse wegnehmen, die deine App schon nutzt. Die übrigen Seiten (`/files`, `/chat`, `/people/{id}`, `/profile`, `/settings`, `/notifications`, `/assistant`, `/workflows/approvals`, `/search`, `/data/export-schedules`, `/reports`) behalten ihre kurzen Adressen.

```csharp
builder.Services.AddCoworkeeClient(baseAddress, options =>
{
    options.PagePrefix = "/cw";                        // Standard; "" behält die alten /admin/...-Adressen
    options.Pages.Replace<MyUsers>("users")              // deine Komponente unter der Adresse der Bibliothek, mit denselben Parametern
        .Replace("user", "/team/{Id}")                // Links und alte Adressen führen zu deiner eigenen Seite
        .Disable("backups");                            // Menüeintrag und Adresse sind weg
});
```

Eine eigene App, die die Administration nicht erreichen darf (ein Kunden-Frontend), behält nur die Seiten, die sie nennt, und schaltet alle anderen ab; `Only()` ohne Schlüssel behält keine:

```csharp
options.Pages.Only("profile");   // nur die Profilseite der Bibliothek; /cw/admin/... und der Rest finden nichts, ihre Links und Menüeinträge verschwinden
options.Pages.Only();            // gar keine Seite der Bibliothek
```

Lässt du die Assembly der Bibliothek aus den `AdditionalAssemblies` des Routers (und aus `AddAdditionalAssemblies` des Hosts) heraus, fällt auch die Sammelroute weg: Dann findet selbst die Adresse einer behaltenen Seite nichts, der Router zeigt sein `NotFound`.

Alte Adressen funktionieren weiter: `/admin/users/…` in einem Lesezeichen, einer Mail oder einer Push-Benachrichtigung leitet auf die aktuelle Adresse weiter. Menü, Befehlspalette und die Links zwischen den Seiten fragen dieselbe Registrierung (`CoworkeePages.Href(path)`), eine ersetzte oder abgeschaltete Seite ist also überall ersetzt oder weg. Links vom Server (Benachrichtigungen, Mails) nutzen die alte Adresse, der Client löst sie auf.

So funktioniert es: eine Sammelseite der Bibliothek (`CoworkeePageHost`, Route `/{*Path}`) zeigt die Seiten der Bibliothek. Seiten deiner App gewinnen immer, eine eigene `/admin/ai`-Seite stößt also nie mit der Bibliothek zusammen. Eine Adresse, die niemand kennt, zeigt die `NotFoundPage` des Routers, wenn du eine gesetzt hast, sonst ein kurzes „Nicht gefunden.“ im Layout.

Schlüssel: `users`, `user`, `groups`, `roles`, `role`, `custom-permissions`, `clients`, `scopes`, `api-keys`, `sessions`, `settings`, `sign-in`, `ai-settings`, `ai-tool-calls`, `audit`, `security-log`, `request-log`, `backups`, `data-transfer`, `export-schedules`, `editions`, `languages`, `translations`, `mail-settings`, `mail-templates`, `mail-template`, `mail-campaigns`, `mail-log`, `send-notification`, `retention`, `services`, `system-status`, `tenants`, `themes`, `header`, `dialog-options`, `webhooks`, `approvals`, `assistant`, `chat`, `person`, `files`, `notifications`, `profile`, `search`, `user-settings`, mit Reporting dazu `reports`, `report`, `report-new`, `report-subscriptions`, `report-data`, mit Client-Entities `local-copies`. Ein unbekannter Schlüssel hält die App beim Start an.

### Ein eigenes Personenprofil

Die Personenseite (`/people/{id}`, Schlüssel `person`) ist das Ziel des Eintrags *Profil* in jedem Avatar-Menü und der `@`-Erwähnungen im Chat. Eine App mit eigenem Personenprofil (etwa eine HR-App mit Mitarbeiterakten) schickt sie dorthin:

```csharp
options.Pages.Replace("person", "/hr/people/{Id}");   // Links gehen auf deine Seite; {Id} ist die Benutzer-Id
```

Deine Seite nimmt die Benutzer-Id entgegen und sucht ihren eigenen Datensatz. Soll deine Seite selbst unter `/people/{id}` liegen, gib ihr diese Route (`@page "/people/{Id:guid}"`): Seiten der App gewinnen gegen die der Bibliothek, die Links bleiben, wie sie sind. `options.Pages.Replace<MyPerson>("person")` rendert stattdessen deine Komponente unter der Adresse der Bibliothek.

## Ersetzen oder beitragen

Nimm die Erweiterungspunkte, wenn sie passen: `INavigationContributor` für Menüeinträge, `IAppBarContributor` für die obere Leiste, `NavigationMenuOptions` zum Ausblenden und Sortieren. Ersetze eine Komponente, wenn du anderes Markup oder Verhalten brauchst.
