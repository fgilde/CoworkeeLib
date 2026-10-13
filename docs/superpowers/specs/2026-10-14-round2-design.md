# Runde 2 – Design (Owner-Feedback 2026-10-14)

Lib `C:\dev\privat\github\coworkee` (main), Demo `C:\dev\privat\github\cleanarchitecturebaseblazor` (master), MudEx `C:\dev\privat\github\MudBlazor.Extensions` (lokaler Branch `feat/coworkee-p4`, nie pushen). Regeln aus `CONTINUE.md`.

## A. Chip-Liste sauber in MudEx (`MudExTagField`)
`MudExTagField<T>` bekommt, was die OAuth-Felder brauchen: Vorschläge (`SearchFunc` mit Dropdown, gewählte ausgeblendet), Prüfung je Eintrag (`ValidateChip` → Fehlertext, ungültige Eingabe bleibt im Feld), feste Chips (`IsChipClosable`), gespeicherte ungültige Werte als Fehler-Chip. Commit-Verhalten wie im Lib-Editor im Browser erprobt: Enter/Trenner/Einfügen/Vorschlag übernehmen; Blur übernimmt nur, wenn das Feld wirklich verlassen wird; Liste schließt wegen keiner Treffer → nichts übernehmen; Escape verwirft. Sample-Seite erweitert. Die Lib behält `ChipListEditor`, bis der Owner MudEx released; danach Umstellung (Follow-up in CONTINUE.md).

## B. AspireUI als Option
Owner-Entscheidung: **keine** Lib-API. CLI-Option `aspireUI` (Bool, Gruppe Infrastructure, Default false) fügt im Template per `#if (aspireUI)` genau den Code der Demo in `MyApp.AppHost/AppHost.cs` ein (`builder.AddAspireUI().WithAdminUser(administrator.Email, administrator.Password).WithSeedFromDirectory(builder.AppHostDirectory).WithCoworkeeSignOn(app).WithRetrieveApi();`, `WithCoworkeeSignOn` nur mit Keycloak) plus Paketverweis `Nextended.Aspire.Hosting.AspireUI` und nötige `using`s; LOGIN.md-Abschnitt, `SignInInfo` erkennt `AddAspireUI`; Studio zeigt die Option (hervorgehoben neben Grafana/WebDataStudio, Preset „Full“ an). Demo bleibt unverändert.

## C. Logo im Theme
`ThemeDefinition.LogoSvg` wird zu `Logo` (Text, SVG-Markup **oder** Bild-Data-URL png/jpeg/webp/svg, max. 512 KB; SVG weiter bereinigt wie bisher). EF-Änderung mit Migration (Daten von `LogoSvg` übernehmen) → Template-Migration + Demo-Migration. Leer = App-Logo (`CoworkeeClientOptions.AppLogo` bzw. `AuthServerOptions.LogoUrl`). ThemeEdit rendert `Logo` mit einer eigenen Komponente: Vorschau + Button „Logo wählen“ → Dialog mit `MudExUploadEdit` (Cloud-Picker wie überall) → Data-URL; „Zurücksetzen“ = App-Logo. Logo gilt für AppBar, NavMenu, Setup, About, AuthServer-Seiten und als Favicon (dynamisch, wenn gesetzt).

## D. Keycloak-Anmeldeseite im App-Design
Coworkee erzeugt ein Keycloak-Login-Theme `coworkee-{app}` (Parent `keycloak.v2`, eigene CSS mit den Farben des Standard-Themes hell/dunkel, Logo, App-Name, Hintergrundbild/-video und Layout der Anmeldeseite per kleinem Skript, keine Template-Overrides).
- Aspire: Theme-Ordner `{AppHostDirectory}/.coworkee/keycloak-themes` wird in den Keycloak-Container gemountet (`/opt/keycloak/themes`); beim Start wird das Theme aus den Optionen erzeugt, falls noch keins existiert (App-Name, Logo, Ocean-Farben, `SignIn`-Design). Realm-Reconcile setzt `loginTheme` und `displayName` (neu und bestehend). Damit ist nach dem Anlegen per CLI/Studio alles direkt sichtbar.
- Laufzeit: der API-Host bekommt `Coworkee:Keycloak:ThemeDirectory` (+ App-Name); Speichern des Standard-Themes oder des Sign-in-Designs schreibt das Theme automatisch neu; Admin → Themes zeigt den Status und einen Button „Nach Keycloak übernehmen“. Dev-Keycloak (start-dev) cached Themes nicht → sofort sichtbar. Produktion: geteiltes Volume + Keycloak-Neustart, dokumentiert.
- Medien: absolute URLs der Web-App; Logo aus dem Theme als Datei im Theme-Ordner.

## E. OAuth-App duplizieren
Zeilenaktion „Duplizieren“ öffnet den Anlegen-Dialog vorbefüllt (Client-ID `{id}-copy`, eindeutig gemacht; Anzeigename „… (Kopie)“), auch für Apps aus der Konfiguration. Secret wird neu erzeugt.

## F. Navigation
Mail-Einstellungen unter Administration → Kommunikation.

## G. Tabs + Toolbar
Settings-Tabs bündig mit dem Inhalt: Tab-Leiste und Inhaltskarte gleich breit, oben abgerundet, wirken als eine Einheit; Sticky-Merge bleibt. Toolbar-Werkzeuge in allen Settings-Formularen rechts (`SettingsForm` fehlt der Spacer).

## Entscheidungen des Builders
Option `aspireUI` Default aus; Logo-Limit 512 KB; Keycloak-Theme per CSS+JS statt FreeMarker-Override; automatische Übernahme + manueller Button; Favicon folgt dem Theme-Logo; Mails bleiben ohne Logo.
