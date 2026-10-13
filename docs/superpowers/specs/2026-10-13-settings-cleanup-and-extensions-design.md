# Settings aufräumen und Lib-Erweiterungen – Design

Stand 2026-10-13. Lib `C:\dev\privat\github\coworkee` (main), Demo `C:\dev\privat\github\cleanarchitecturebaseblazor` (master), MudEx `C:\dev\privat\github\MudBlazor.Extensions` (nur lokale Branches, nie pushen). Regeln aus `CONTINUE.md` gelten (CCD, `.razor` + `.razor.cs`, wenige Kommentare, Docs EN + `.de.md`, keine Attribution in Commits).

## Warum

Die Admin-Einstellungen sind gewachsen und unaufgeräumt: Bugs (Dauer-Reload, falsche Maskierung, abgeschnittenes Popup), redundante oder unverständliche Felder, fehlende Abhängigkeiten zwischen Feldern. Gleichzeitig fehlen Dinge, die eine produktive App braucht: Anmeldemodus und Passwortregeln zur Laufzeit, mehrere Mail-Anbieter, vollständige Sprachen, komfortable Mehrfachwert-Felder, ein programmatisch nutzbares Service-Verzeichnis inkl. AspireUI.

Erfolg: Owner öffnet die Demo, sieht jede Änderung sofort, Settings sind verständlich und stabil, Lib-Nutzer haben dokumentierte APIs.

## Nicht-Ziele

- Externe Login-Anbieter zur Laufzeit hinzufügen (bleiben Startup-Registrierung).
- Alle Lib-Texte in neue Apps kopieren.
- Newsletter/Marketing-Mails (nur transaktionaler Versand).
- MudEx pushen oder releasen.

## Vorgehen

Ein Branch pro Paket in der Lib (`feat/...` bzw. `fix/...`), zweigt von `main` ab; wenn grün, Merge nach `main` (Owner-Entscheidung), dann lokal packen, Demo-Commit auf master, kurze Meldung an den Owner, nächstes Paket von `main`. Reihenfolge = Nummerierung unten. Jedes Paket: Build + Tests grün, Demo nachgezogen, Docs EN/DE wo API/Verhalten neu ist. EF-Modelländerungen → Template-Migration + Demo-Migration (siehe CONTINUE.md).

---

## Paket 1 – Bugs und Settings aufräumen

### 1.1 Dauer-Reload App-Einstellungen
`AppConfigurationEditor.razor.cs:40` lädt in `OnParametersSetAsync` bei jedem Parameter-Set neu (neues Dictionary aus `Settings.razor:12-13` via `DynamicComponent`, MudTabs-Resize → Re-Render → Reload-Schleife; `@key="_revision"` baut das Formular neu). Fix: nur laden, wenn `Registration.Section` sich gegenüber dem zuletzt geladenen Wert ändert. Gleiches Muster in `SettingsForm` prüfen und gleich absichern.

### 1.2 Maskierung
`Coworkee.Settings\AppConfiguration.cs:392` Regex `password|secret|apikey|api_key|token|connectionstring` trifft `ConnectionStringName`. Fix: auf Namensende verankern (`(password|secret|apikey|api_key|token|connectionstring)$`, case-insensitive). Test: `ConnectionStringName` sichtbar, `Password`, `ApiKey`, `ClientSecret`, `ConnectionString` maskiert.

### 1.3 Cron-Popup
`CronEditor.razor:30-31` `Style="max-width:140px"` landet in MudBlazor auch am Picker-Popup. Fix: Breite über `Class` / Wrapper. (Neuer MudEx-Editor siehe Paket 4.)

### 1.4 Abhängige Felder (generisch)
`SettingDefinition` bekommt optional `EnabledBy` (Name eines Bool-Settings). `SettingsModel.Configure` setzt dafür `AsDisabledIf` auf das Feld, wenn das Bool-Feld `false` ist. Anwenden auf: Google Drive / OneDrive / Dropbox (ClientId/AppKey ← `…Enabled`), Übersetzung (alle Felder ← Anbieter gesetzt bzw. sinnvoller Schalter, Entscheidung beim Bauen). Prüfen, ob MudEx die Bedingung bei Checkbox-Änderung live neu auswertet; falls nicht → Fix in MudEx-Branch (gleicher Branch wie Paket 4).

### 1.5 AI aus System-Tab
AI-Settings im System-Tab nicht mehr rendern (Seite `/admin/ai-settings` existiert). `Ai.McpEnabled` vorher auf die AI-Seite übernehmen. Mechanik: Settings-Definitionen bekommen ein Kennzeichen „eigene Seite“ (z. B. `Hidden`/`Page` auf Gruppe), `SettingsForm` blendet solche Gruppen aus. Gilt danach auch für Mail (Paket 6).

### 1.6 Standardsprache
`Localization.Culture` (Global/Tenant/User) = Standardsprache der UI (Fallback System → Organisation → Benutzer) und Sprache der Digest-Mail. Bleibt. Anzeige: Label „Standardsprache“ / „Default language“, Beschreibung erklärt Fallback, Render als `MudExSelect<string>` über die konfigurierten Sprachen (Vorlage `UserFormMeta.cs:25-29`), im Tenant-Scope mit Leer-Option „Systemstandard“.

### 1.7 Übersetzung: Leerraum + Expand-Icon
Ursache unbekannt → im Browser (Playwright) analysieren und an der Wurzel fixen.

### 1.8 Organisation-Tab
Bleibt mit Standardsprache (Select) und Farbmodus. Mail-Felder wandern auf die Mail-Seite (Paket 6).

---

## Paket 2 – Anmeldemodus und Passwortregeln

### 2.1 LoginMode als Setting
Enum `LoginMode { Internal, External, Both }` bleibt (Name passt; hängt in der Lib nicht an Keycloak). Neues Global-Setting `Auth.LoginMode` (Choice), Gruppe Sicherheit/Anmeldung. Default = `Coworkee:Auth:External:Mode` (Aspire `CoworkeeKeycloakOptions.LoginMode` setzt das weiter). Sichtbar/editierbar nur, wenn mindestens ein externer Anbieter konfiguriert ist (ohne Anbieter wirkt ohnehin `Internal`). `Login.cshtml.cs` und `Register.cshtml.cs` lesen den Modus über `ISettingProvider` statt `IOptions`. Schutz: kein Aussperren – Modus `External` nur, wenn Anbieter vorhanden (bestehende Logik `Providers.Count == 0 → Internal` bleibt).

### 2.2 Passwortregeln als Settings
Neue Global-Settings, Gruppe Sicherheit: `Account.Password.RequiredLength` (Default 8), `RequiredUniqueChars` (1), `RequireDigit`, `RequireLowercase`, `RequireUppercase`, `RequireNonAlphanumeric` (Defaults = heutiges Verhalten). Defaults aus `Coworkee:Registration:Password` (Config) übernehmen, falls gesetzt.
Nur global, kein Tenant-Override (Builder-Entscheidung). Durchsetzung überall (Auth-Server, API `SetUserPassword`, Setup, Reset, Change): eigener `IPasswordValidator<User>`, der die Settings liest und die Identity-Prüflogik mit diesen Optionen ausführt; ersetzt den Standard-`PasswordValidator<User>`. Fest verdrahtete `MinimumLength(8)` (Setup-Validator) entfernen bzw. auf Settings stützen; CLI-Spec-Validator (`SpecValidator.Users.cs:13`) bleibt (Seed-Zeitpunkt, keine Settings).
Client: Regeln als client-sichtbare Settings, Passwortfelder (Setup, Benutzer-Passwort setzen) zeigen Hinweistext mit den Regeln.

---

## Paket 3 – Sticky-Merge Tabs + ObjectEdit-Toolbar

Wiederherstellen des alten Verhaltens (Demo-Commit `2e4c65c`, Stand `b4eecad`): Beim Scrollen verschmelzen Tab-Leiste und ObjectEdit-Toolbar zu einer Sticky-Leiste; beim Hochscrollen wieder normal. Umsetzung in der Lib als wiederverwendbare Klasse `cw-tabs-stick-merge` + kleines JS-Modul (IntersectionObserver setzt `stuck` auf `.mud-tabs-tabbar`), keine `eval`-Interop. Einsatz: Admin → Settings (alle Tabs). Breite der gemergten Tab-Leiste darf die Toolbar-Aktionen nicht überdecken (alt: 450px fix; besser dynamisch bzw. Toolbar-Aktionen rechts). Dark/Light und Mobile prüfen.

---

## Paket 4 – Cron-Editor in MudEx

In MudEx (lokaler Branch, z. B. `feat/cron-edit`): Komponente `MudExCronEdit` (Name final beim Bauen, MudEx-Konventionen folgen), bindet an `string` (`@bind-Value`), Funktionsumfang wie Coworkee `CronEditor` (inkl. `CronSchedule`-Logik), Popup-Bug nicht übernehmen, Lokalisierung über MudEx-Mechanik. Sample-Seite im MudEx-Sample-Projekt (wie `Page_TagField.razor`), Eintrag in Navigation, ggf. `IDefaultRenderDataProvider`-Registrierung für ObjectEdit analog MudEx-Konventionen.
Lib: bleibt vorerst auf eigenem `CronEditor` (mit Fix 1.3). Nach MudEx-Release durch Owner: Lib stellt `[Cron]` auf MudEx-Komponente um und löscht eigene Implementierung (Follow-up in CONTINUE.md vermerken).

---

## Paket 5 – Mehrfachwerte in OAuth-App (und Scopes) als Chips

`Clients.razor.cs` `ClientForm`: `RedirectUris`, `PostLogoutRedirectUris`, `Scopes` von `string` auf `List<string>` (API ist bereits `IReadOnlyList<string>`). `ScopeForm.Resources` analog.
UI: zuerst `MudExSelect<string>` mit `MultiSelection` + `ValuePresenter=Chip` prüfen. Anforderungen: Vorschläge (Scopes: OIDC-Standard openid/profile/email/roles/offline_access/phone/address + Scopes aus dem Store), Freitext erlaubt (URIs immer, Scopes zusätzlich), Entfernen per Chip-Close. Reicht MudExSelect nicht (vermutlich kein Freitext): fehlende, allgemein sinnvolle Fähigkeit in MudEx einbauen (Branch aus Paket 4; z. B. `MudExTagField` mit Vorschlagsliste); bis zum Release eine Lib-Komponente (abgeleitet/analog `ContentTypesEditor`), die danach ersetzt wird. Gute MetaConfig: Labels, Hilfetexte, Validierung (absolute URIs, Scope-Regex wie Server).
`openid` bleibt erzwungen. Client-seitige Liste der verfügbaren Scopes: bestehendes `GetScopes` + Standardliste.

---

## Paket 6 – Mail-Anbieter + Mail-Seite

Öffentliche Schnittstelle `IMailSender.QueueAsync(...)` bleibt unverändert (Queue, Templates, Retry, Whitelist bleiben).
Transport: `ISmtpTransport` → `IMailTransport` (Umbenennung ohne Obsolete-Shim, 0.0.x; Builder-Entscheidung). Bestehende Keys `Mail.Smtp.*` bleiben unverändert, keine Datenmigration. Tenant-Override: neues Tenant-Setting `Mail.UseOwnServer`; an → alle Mail-Werte (Anbieter, Zugangsdaten, From, Whitelist) aus Tenant-Scope, aus → alle aus Global; keine Feldmischung; Ausschalten löscht nichts (Builder-Entscheidung). Implementierungen: (Implementierungen `Smtp` (MailKit, bestehend), `SendGrid`, `Mailgun`, `MailchimpTransactional` (Mandrill)). Drittanbieter direkt per `HttpClient` gegen deren REST-API, keine neuen Pakete. Auswahl per Setting `Mail.Provider` (Choice, Default `Smtp`). Anbieterspezifische Settings: SendGrid ApiKey; Mailgun ApiKey, Domain, Region (US/EU); Mandrill ApiKey; gemeinsam `Mail.From`, `Mail.Whitelist`. Secrets als `SettingType.Secret`. Scopes Global + Tenant wie heute (Tenant-Override pro Organisation bleibt).
`MailSetupCheck` und Fehlermeldungen anbieterabhängig. Erweiterbar: App kann eigenen `IMailTransport` registrieren (Doku).
Seite `/admin/mail-settings` (analog `/admin/ai-settings`, Nav-Eintrag, Permission `Settings.Manage`): Anbieter-Select, nur passende Felder, Button „Testmail senden“ (an aktuellen Benutzer, sofort statt Queue, zeigt Fehler). In einer Organisation (nicht System) Schalter „Eigener Mailserver“ → Tenant-Override an/aus. Mail-Gruppen verschwinden aus System-/Organisation-Tab (Mechanik 1.5).
Tests: je Anbieter Request-Aufbau gegen gefakten `HttpMessageHandler`. Docs EN/DE.

---

## Paket 7 – Service-Verzeichnis + AspireUI

Befund: `GET /api/retrieve/apps` (AspireUI, aktiviert per `WithRetrieveApi`, Nextended 10.1.45 auf nuget.org) liefert die von AspireUI **gehosteten** Stacks/Apps (`[{name, kind, healthy, urls}]`), nicht die Ressourcen des Eltern-AppHosts (die zeigt die Services-Seite bereits).
- Nextended auf 10.1.45 heben.
- Öffentlicher Backend-Service `IServiceDirectory` (Coworkee.Settings oder passendes Modul): `GetAllAsync(ct)` (inkl. Health) und `ResolveAsync(name, ct)` → Eintrag mit URL(s) oder null. Quellen über `IServiceSource` (erweiterbar): konfigurierte Services (`Coworkee:Services`, bestehend) und AspireUI-Quelle. `ServiceDto` um Herkunft/Quelle und ggf. mehrere URLs erweitern; Namen der AspireUI-Einträge eindeutig (z. B. Präfix der AspireUI-Ressource). Der interne Handler `GetServices` und die Services-Seite nutzen denselben Service; Seite gruppiert nach Quelle.
- Erkennung: Aspire-Integration (`CoworkeeApp.Services.cs`) erkennt eine AspireUI-Ressource mit aktivierter Retrieve-API (Annotation/Env `ASPIREUI_RETRIEVE_API`, optional Key) und injiziert `Coworkee__AspireUI__Url` und `Coworkee__AspireUI__ApiKey` (Key aus Parameter/Env der Ressource). Ohne Aspire: gleiche Keys in appsettings. Ohne Konfiguration ist die Quelle inaktiv. Fehler/Timeout der API → Quelle liefert leer + Log, Seite zeigt Hinweis.
- Kurzes Caching (Sekunden), Timeout 3 s wie bestehend.
- Docs EN/DE: wie eine App Dienste auflöst (`IServiceDirectory` injizieren, Beispiel), Quellen, AspireUI-Einrichtung (`WithRetrieveApi`), eigene Quelle registrieren.
- Tests: Aspire-Env-Injektion, AspireUI-Quelle gegen Fake-Handler, Directory-Merge.

---

## Paket 8 – Sprachen + Text-Export

Sprachen (vollständig, vom Agenten selbst übersetzt; der alte maschinelle YAML-Bestand liegt nur in der Git-Historie des Demo-Repos, nichts zu löschen): ar, bg, cs, da, de, el, en (Keys), es, fr, hr, id, it, ja, km, ko, nl, pl, pt, ru, sv, tr, uk, zh (23, Owner-Entscheidung). Umfang: Lib-Texte (`Coworkee.Localization`), AuthServer-Texte (eigener Mechanismus `AuthTexts` bleibt, nur mehr Sprachen), Mail-Vorlagen (Scriban subject/body), Template-App-Texte (Template enthält nur die per `languages` gewählten Sprachen). `OptionCatalog.ShippedLanguages` und Sprachkatalog (u. a. `km` ergänzen) anpassen. Vollständigkeitstest deckt Lib- und AuthServer-Texte ab. Platzhalter/Formatierungen ({0}, HTML) müssen erhalten bleiben.
Test: jede ausgelieferte Sprache enthält jeden Key (Referenz = Vereinigung aller Keys aus `de.json`), Platzhalter identisch; zusätzlich Test, dass im Code verwendete Keys in `de.json` stehen, soweit statisch ermittelbar (sonst weglassen).
CLI: `coworkee texts export --lang <code> [--keys <filter>] [--out <path>]` schreibt Lib-Texte in die App-`Localization/<code>.json` (merge, vorhandene App-Werte bleiben). Template bleibt schlank. Docs EN/DE: Override-Mechanik (gleicher Key in App-JSON gewinnt, Laufzeit-Edits unter `/admin/translations` gehen vor), Export-Befehl.

---

## Dem Builder überlassen
Interne Namen, Dateiaufteilung, genaue Setting-Keys (konsistent zum Bestand), Test-Zuschnitt, CSS-Details des Sticky-Merge, Sprachdatei-Format bleibt JSON wie Bestand.
