# Paket 2 – Anmeldemodus und Passwortregeln: Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Admins stellen den Anmeldemodus (nur wenn externe Anbieter konfiguriert sind) und die Passwortregeln zur Laufzeit in den Einstellungen ein; die Regeln gelten überall und werden in den Formularen angezeigt.

**Architecture:** Neues Global-Setting `Auth.LoginMode` (Choice), nur definiert, wenn in `Coworkee:Auth:External:Providers` mindestens ein Anbieter steht; Default = `Coworkee:Auth:External:Mode`. Login/Register lesen es per `ISettingProvider`. Passwortregeln als Global-Settings in der Gruppe „Security“; ein `SettingsPasswordValidator : IPasswordValidator<User>` ersetzt Identitys `PasswordValidator<User>` und prüft mit den Settings. Client-sichtbare Regeln liefern Hinweistexte.

**Tech Stack:** .NET 10, ASP.NET Core Identity, Razor Pages (AuthServer), Blazor WASM, Aspire, xunit/bUnit/Shouldly.

**Spec:** `docs/superpowers/specs/2026-10-13-settings-cleanup-and-extensions-design.md` (Paket 2)

## Global Constraints

- Branch `feat/login-mode-password-rules` von `main`; nach grünem Build + Tests Merge nach `main`.
- CCD, `.razor` + `.razor.cs`, kleine Dateien, keine erklärenden Kommentare außer nicht offensichtlichem Warum.
- Keine Attribution in Commits; Conventional Commits, englisch.
- Neue UI-Texte: englischer Key + Deutsch in `src/Coworkee.Localization/Localization/de.json`; AuthServer-Texte in `src/Coworkee.AuthServer/Texts/de.json`.
- Docs EN + `.de.md` (echte Umlaute); nie „ABP“/„CleanArchitectureBase“.
- Passwortregeln nur Global (kein Tenant-Override).
- Testrunner: `--filter-class` / `--filter-method`. Docker vorhanden.

## Review Focus

- Kein Aussperren: `Auth.LoginMode = External` ohne konfigurierten Anbieter (Setting aus alter DB, Anbieter später entfernt) → Login zeigt Passwortformular (`Internal`).
- Gespeicherter LoginMode-Wert, der kein gültiger Enum-Name ist → Default aus Config.
- Admin setzt `RequiredLength` 0 oder negativ → mindestens 1 erzwungen; `RequiredUniqueChars` > Länge ist erlaubt (Identity-Verhalten).
- Seed/Setup (erster Admin) auf leerer DB: Settings-Tabelle leer → Defaults greifen, Setup klappt mit bisherigem Demo-Passwort.
- `PasswordHistoryValidator` bleibt zusätzlich aktiv; Fehlermeldungen lokalisiert über `LocalizedIdentityErrorDescriber`.

---

### Task 1: Anmeldemodus als Setting

**Interfaces:**
- Produces: `public static class AuthSettings { public const string LoginMode = "Auth.LoginMode"; }` (Contracts, Namespace wie `LoginMode`-Enum `src/Coworkee.Contracts/Configuration/LoginMode.cs`). Choice-Werte = Enum-Namen `Internal`, `External`, `Both`.

**Files:**
- Create: Definition-Contributor im Modul, das in **jedem** Host geladen wird, der die Settings-API (`/api/v1/settings`) bedient **und** im AuthServer-Host (vorher prüfen: welcher Host/Modul hostet `CoworkeeSettingsModule`; wo `AuthServerOptions` gebunden ist). Er liest `IConfiguration` Abschnitt `Coworkee:Auth:External` (`AuthServerOptions.Section` + `External`): definiert `Auth.LoginMode` (Gruppe „Security“/„Sign-in security“, DisplayName „Sign-in methods“, Description „Password, external providers (e.g. Keycloak) or both. Shows only when an external provider is configured.“, Choices Enum-Namen, Default = konfigurierter `Mode`), **nur** wenn mindestens ein Provider konfiguriert ist.
- Modify: `src/Coworkee.AuthServer/Pages/Account/Login.cshtml.cs:25` und `Register.cshtml.cs:114` – Modus aus `ISettingProvider` (Fallback Config, ungültig → Config; ohne Provider weiterhin `Internal`). Gemeinsame Auflösung einmal (z. B. `LoginModes.ResolveAsync(settings, options, ct)` im AuthServer), nicht doppelt.
- Modify: `src/Coworkee.Aspire/CoworkeeApp.Keycloak.cs:66-72` – falls der Settings-API-Host nicht der Auth-Host ist: `Mode` und Provider-`DisplayName` (keine Secrets) zusätzlich an diesen Host geben, damit dort die Definition erscheint. Aspire-Test in `tests/Coworkee.Aspire.Tests` anpassen/ergänzen.
- Test: `tests/Coworkee.AuthServer.Tests` (Login-Seite zeigt/verbirgt Passwortformular nach Setting), `tests/Coworkee.Settings.Tests` oder passendes Projekt (Definition nur mit Provider).

- [ ] **Step 1: Failing tests**
  - `Login_mode_setting_hides_the_password_form`: Host mit einem Fake-Provider, Setting `Auth.LoginMode = External` → Login-HTML ohne Passwortfeld; `Internal` → ohne Provider-Buttons.
  - `Without_providers_the_password_form_always_shows`: Setting `External`, keine Provider → Passwortfeld vorhanden.
  - `Login_mode_is_defined_only_with_an_external_provider`: Definitionen ohne Provider enthalten `Auth.LoginMode` nicht, mit Provider ja, Default = konfigurierter Mode.
- [ ] **Step 2: FAIL**
- [ ] **Step 3: Implementieren**
- [ ] **Step 4: PASS** + AuthServer-, Settings-, Aspire-Tests grün
- [ ] **Step 5: Commit** `feat(auth): sign-in methods are a setting when an external provider is configured`

### Task 2: Passwortregeln als Settings, überall durchgesetzt

**Interfaces:**
- Produces in `src/Coworkee.Account/PasswordPolicy.cs` → `SecuritySettings`: `PasswordRequiredLength = "Account.Password.RequiredLength"`, `PasswordRequiredUniqueChars = "Account.Password.RequiredUniqueChars"`, `PasswordRequireDigit = "Account.Password.RequireDigit"`, `PasswordRequireLowercase = "Account.Password.RequireLowercase"`, `PasswordRequireUppercase = "Account.Password.RequireUppercase"`, `PasswordRequireNonAlphanumeric = "Account.Password.RequireNonAlphanumeric"`. Die Namen auch in Contracts verfügbar machen, wenn der Client sie braucht (Task 3) – eine Quelle, `SecuritySettings` verweist darauf.
- Produces: `internal sealed class SettingsPasswordValidator(ISettingProvider settings) : IPasswordValidator<User>`.

**Files:**
- Modify: `src/Coworkee.Account/PasswordPolicy.cs` (Definitionen in `SecuritySettingDefinitions`, Typen Int/Bool, `visibleToClient: true`, Defaults aus dem konfigurierten `IdentityOptions.Password` – heute `RequiredLength = 8`, Rest Identity-Defaults; Config `Coworkee:Registration:Password` wird schon gebunden, `Account.cs:162`).
- Create: `src/Coworkee.Account/SettingsPasswordValidator.cs` – Prüfungen wie Identitys `PasswordValidator<User>` (Länge, Ziffer, Klein, Groß, Nicht-alphanumerisch, eindeutige Zeichen) mit Fehlern aus `manager.ErrorDescriber`; `RequiredLength` min. 1.
- Modify: Registrierung (`src/Coworkee.Account/Account.cs` bzw. wo `PasswordHistoryValidator` registriert ist): Identitys `PasswordValidator<User>` entfernen, `SettingsPasswordValidator` registrieren. Prüfen, dass jeder Host mit `UserManager` (API, AuthServer, Migrations-Seed) das Account-Modul hat; sonst dort fehlende Regeln melden (Report).
- Modify: `src/Coworkee.Identity/Setup/SetupHandlers.cs:27` – `MinimumLength(8)` entfernen (Identity-Validierung beim Anlegen meldet die Regeln).
- Test: `tests/Coworkee.Account.Tests` o. ä. (Projekt suchen, in dem `PasswordHistoryValidator` getestet wird).

- [ ] **Step 1: Failing tests**
  - `Password_rules_come_from_the_settings`: Setting `RequiredLength=12`, `RequireDigit=false` → Passwort „abcdefghijK!“ ok, „abcdefK!“ (8) abgelehnt mit `PasswordTooShort`.
  - `Admin_set_password_follows_the_settings`: API `SetUserPassword` mit Passwort, das gegen geänderte Regel verstößt → 400 mit Fehler.
  - `Required_length_is_at_least_one`: Setting 0 → leeres Passwort abgelehnt.
- [ ] **Step 2: FAIL**
- [ ] **Step 3: Implementieren**
- [ ] **Step 4: PASS** + Account-, Identity-, AuthServer-Tests grün (Setup-Tests!)
- [ ] **Step 5: Commit** `feat(account): password rules are settings and apply to every way a password is set`

### Task 3: Regeln als Hinweis in den Formularen

**Interfaces:**
- Consumes: Setting-Namen aus Task 2 (client-sichtbar über `GET /api/v1/settings/client` bzw. `ClientSettings` im Client).
- Produces: `public sealed record PasswordRules(int RequiredLength, int RequiredUniqueChars, bool RequireDigit, bool RequireLowercase, bool RequireUppercase, bool RequireNonAlphanumeric)` mit `static PasswordRules From(IReadOnlyDictionary<string, string?> values)` in Contracts; Defaults = Identity-Defaults mit Länge 8.

**Files:**
- Client: Hinweistext (z. B. „At least 12 characters, a digit, an uppercase letter.“, lokalisiert, aus Teilen zusammengesetzt) als `HelperText` an den Passwortfeldern in `src/Coworkee.Client.Blazor/Pages/Setup.razor` (Admin-Passwort) und dem Admin-Dialog „Passwort setzen“ (`Pages/Admin/UserPasswordForm.cs` + Meta). Eine Client-Hilfsmethode, die aus `PasswordRules` + Localizer den Text baut.
- AuthServer: gleicher Hinweis (über `AuthTexts`) unter dem Passwortfeld in `Register`, `ResetPassword`, `Manage/ChangePassword`, `ChangePasswordRequired` – Regeln via `ISettingProvider`.
- Achtung Setup: vor dem Setup ist der Benutzer nicht angemeldet; prüfen, ob Client-Settings dann lesbar sind; sonst Defaults.
- Test: bUnit (`Setup` zeigt Hinweis aus gestubbten Client-Settings), AuthServer-Test (Register-HTML enthält Hinweis mit konfigurierter Länge), Unit-Test `PasswordRules.From`.

- [ ] **Step 1: Failing tests** (s. o.)
- [ ] **Step 2: FAIL**
- [ ] **Step 3: Implementieren** + de.json (beide)
- [ ] **Step 4: PASS**
- [ ] **Step 5: Commit** `feat(account): password fields show the rules that apply`

### Task 4: Docs, Demo, Merge

- [ ] Docs: `website/docs/modules/settings.md` (+de) oder Account-/Auth-Seite: Anmeldemodus-Setting, Passwortregeln. `python -m mkdocs build --strict -f website/mkdocs.yml -d <tmp>` grün.
- [ ] Voller Build + relevante Testprojekte grün.
- [ ] Merge nach `main`, Pack, Demo prüfen (Settings → Sicherheit: Anmeldemodus mit Keycloak sichtbar; Passwortregeln), Demo-Commit falls Demo-Code betroffen.
