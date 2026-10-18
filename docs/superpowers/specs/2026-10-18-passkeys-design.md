# Passkeys – Design

Lib `C:\dev\privat\github\coworkee` (main), Demo `C:\dev\privat\github\cleanarchitecturebaseblazor` (master), Sharemee `C:\dev\privat\github\shareme` (master). Es gelten die Regeln aus `CONTINUE.md`.

## Warum
Die Anmeldung kennt heute Passwort plus TOTP und externe Provider (Keycloak). Passkeys (WebAuthn) sind phishing-resistent und bequemer. ASP.NET Core Identity 10 bringt die Zeremonien schon mit (`SignInManager.MakePasskeyCreationOptionsAsync`/`PerformPasskeyAttestationAsync`/`MakePasskeyRequestOptionsAsync`/`PasskeySignInAsync`, `UserManager.AddOrUpdatePasskeyAsync`/`GetPasskeysAsync`/`RemovePasskeyAsync`). Wir müssen also verdrahten, nicht selbst bauen.

## Ziele
- Ein Benutzer legt auf der Account-Seite des Auth-Servers einen oder mehrere Passkeys an, benennt sie um und entfernt sie wieder.
- Anmeldung per Passkey: ein Button „Mit Passkey anmelden“ und Conditional UI (Autofill im Benutzernamen-Feld).
- Admins sehen pro Benutzer die Anzahl der Passkeys und können alle entfernen, z. B. bei einem verlorenen Gerät.
- Die Global-Setting `Account.Passkeys.Enabled` (Gruppe Security, Default an) schaltet das Feature ein oder aus.

## Nicht-Ziele
- Registrierung ohne Passwort. Ein Passkey kommt zu einem bestehenden Konto hinzu.
- Passkeys im Keycloak-Modus. Im Login-Modus External macht Keycloak das selbst, dort ist alles ausgeblendet und die Endpoints antworten mit 404.
- Eine Benachrichtigungsmail, wenn ein Passkey angelegt wurde. Kommt später, falls gewünscht.
- Attestation-Prüfung gegen eine Hersteller-Liste. Der Identity-Default bleibt.

## Anti-Ziele
- Kein Inline-Skript: Die CSP des Auth-Servers bleibt `script-src 'self'`.
- Niemand sperrt sich aus: Den letzten Passkey kann nur entfernen, wer noch ein Passwort oder einen externen Login hat.

## Entscheidungen

### Datenmodell
- `IdentityModelContributor` mappt `IdentityUserPasskey<Guid>` als `cw.UserPasskeys`. Schlüssel ist `CredentialId`. Dazu kommen ein Index auf `UserId`, eine Cascade vom User und `Data` als JSON, so wie `IdentityUserContext` es für Schema Version 3 tut.
- `IdentityOptions.Stores.SchemaVersion = IdentitySchemaVersions.Version3`.
- Das muss per Hand passieren, weil `CoworkeeDbContext` kein `IdentityDbContext` ist.
- Funktionieren muss es auf Postgres, SQL Server und SQLite.
- `CoworkeeUserStore` übernimmt die Passkey-Methoden von `UserStore`. Dass gespeichert wird, ist zu prüfen: `AutoSaveChanges = false`, gespeichert wird über die Unit of Work bzw. `db.SaveChangesAsync`, wie in `LoginWith2fa`.

### Auth-Server (Razor Pages)
- **Login** (`Pages/Account/Login.cshtml(.cs)`):
  - Neuer Button „Mit Passkey anmelden“ unter dem Passwortformular.
  - Das Benutzernamen-Feld bekommt `autocomplete="username webauthn"`.
  - Handler `OnPostPasskeyOptionsAsync` liefert die Request-Options als JSON.
  - Handler `OnPostPasskeyAsync` nimmt das Credential-JSON als verstecktes Formularfeld, ruft `PasskeySignInAsync` auf und durchläuft danach dieselben Prüfungen wie die Passwortanmeldung: `IsActive`, `AllowedEmails`, `LastLoginAt`, `LocalRedirect(returnUrl)`. Dafür wird der gemeinsame Teil aus `OnPostAsync` herausgezogen.
  - Gesperrte oder inaktive Konten bekommen dieselben Meldungen wie beim Passwort.
- **2FA:** Eine Passkey-Anmeldung gilt als starke Anmeldung, ein TOTP-Schritt folgt nicht. Das ist das Verhalten von Identity und wird so dokumentiert.
- **Verwaltung:** `Pages/Account/Manage/Passkeys.cshtml(.cs)` (Link in `Manage/Index`):
  - Liste mit Name, Erstellt am und Gerät gesichert (`IsBackedUp`).
  - „Passkey hinzufügen“ (Creation-Options → `navigator.credentials.create` → Attestation → speichern; Name wird abgefragt, Default ist der Browser- bzw. Plattformname), Umbenennen und Entfernen.
  - Höchstens 20 Passkeys pro Benutzer.
- **Script:**
  - Neue Datei `Assets/passkey.js`, in die Whitelist von `AuthAssets.ContentTypes` aufnehmen.
  - Nutzt `PublicKeyCredential.parseCreationOptionsFromJSON`/`parseRequestOptionsFromJSON`, wo vorhanden. Sonst kodiert ein kleiner Fallback base64url selbst.
  - Den Antiforgery-Token schickt es als `RequestVerificationToken`-Header.
  - Ohne WebAuthn-Unterstützung im Browser sind die Buttons unsichtbar.
  - Fehlertexte kommen über `data-text-*` am `<body>`.
- **CSP-Kommentar:** Der veraltete Kommentar „The account pages run no script“ (`CoworkeeAuthServerModule.cs:117`) wird korrigiert.
- **Origin/RP-ID:** `IdentityPasskeyOptions` wird an `Coworkee:Auth:Passkeys` gebunden (`ServerDomain`, Timeouts). Default ist der Host des Requests. Der Auth-Server hat immer einen eigenen Origin (Dev `localhost:7302`, Produktion z. B. `auth.example.com`). Die Doku sagt, wann `ServerDomain` gesetzt werden muss: hinter einem Proxy ohne Forwarded Headers.
- **Rate Limit:** Alle Razor Pages nutzen bereits die Policy `auth`, die neuen Handler damit auch.
- **Abschalten:** Ist die Setting aus oder der Modus External, sind die Buttons und die Verwaltungsseite ausgeblendet und die Handler antworten mit 404. Vorhandene Passkeys bleiben gespeichert.

### Client (Blazor)
- `ProfileSecurity.razor`: neuer Eintrag „Passkeys“ mit Anzahl und einem Button, der `{manage_url}/Passkeys` in einem neuen Tab öffnet, wie bei Passwort und 2FA. Ausgeblendet, wenn die Setting (`visibleToClient: true`) aus ist.
- `ProfileDto` bekommt `PasskeyCount`.
- Admin `UserSignIn.razor`: Zeile „Passkeys: n“ mit dem Button „Alle entfernen“ (Bestätigung). Endpoint `POST /api/v1/identity/users/{id}/passkeys/reset`, analog zu `two-factor/reset`, gleiche Permission. Der Admin-User-DTO bekommt `PasskeyCount`.
- Nach dem Entfernen wird der Security-Stamp erneuert, wie beim 2FA-Reset, falls der das tut.

### Datenschutz
`IdentityPersonalData.ExportAsync` exportiert Passkeys mit Name, Erstellungsdatum und Backup-Status, ohne Public Key. Beim Löschen sorgt die Cascade dafür, dass sie mitgehen.

### Texte
- Auth-Texte in allen 22 `Texts/{lang}.json`.
- Client-Texte in `Coworkee.Localization/Localization/*.json`.
- Die Tests `ShippedLanguagesTests` und `LiteralKeysTests` müssen grün bleiben.

### Template, Demo, Sharemee
- Template-Migration neu erzeugen.
- Demo und Sharemee: eine Migration (`UserPasskeys`) sowie OpenAPI-Snapshot und SDK, falls sich die DTOs ändern.

### Doku und Release
- `website/docs/security/passkeys.md` + `.de.md` (oder ein Abschnitt in der vorhandenen Auth-Seite; entscheidet der Builder): Ablauf, Setting, `ServerDomain`, Verhalten bei 2FA und Keycloak.
- Changelog *Unreleased* mit **Migration nötig**.
- Upgrade-Regel `manual`: Migration hinzufügen.

## Tests
- AuthServer-Tests:
  - Die Options-Handler liefern gültiges JSON.
  - Bei ausgeschalteter Setting oder im External-Modus antworten die Handler mit 404 und die Buttons fehlen.
  - Die Verwaltungsseite listet Passkeys, die im Test direkt über `UserManager.AddOrUpdatePasskeyAsync` angelegt wurden.
  - Der letzte Passkey lässt sich ohne Passwort nicht entfernen.
  - Die CSP ist unverändert.
- Identity-Tests: Mapping über die Provider-Matrix (anlegen, lesen, entfernen) und der Admin-Reset.
- bUnit: Anzeige in `ProfileSecurity` und `UserSignIn`.
- Wenn möglich ein echter Browser-Durchlauf mit Playwright und dem virtuellen Authenticator von Chrome (CDP `WebAuthn.addVirtualAuthenticator`) in einer erzeugten App oder der Demo: Passkey anlegen, abmelden, mit Passkey anmelden.

## Dem Builder überlassen
Layout der Buttons in den drei Sign-in-Designs (Card/Split/Slide), Icon, genaue Texte, ob die Passkey-Doku eine eigene Seite bekommt.
