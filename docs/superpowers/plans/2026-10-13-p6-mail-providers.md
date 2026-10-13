# Paket 6 – Mail-Anbieter und Mail-Seite: Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Mails gehen wahlweise über SMTP, SendGrid, Mailgun oder Mailchimp Transactional (Mandrill); Admins konfigurieren und testen das auf einer eigenen Seite `/admin/mail-settings`, Organisationen können einen eigenen Mailserver nutzen. `IMailSender` bleibt unverändert.

**Architecture:** `ISmtpTransport` wird zu `IMailTransport` (ein Transport je Anbieter, per `Provider`-Name ausgewählt). Ein `MailConnectionResolver` liefert die Verbindung (Anbieter + Werte) komplett aus Tenant- oder Global-Scope, je nach `Mail.UseOwnServer` – keine Feldmischung. HTTP-Anbieter direkt per `HttpClient` (IHttpClientFactory), keine neuen Pakete. Die Settings-Gruppe „Mail“ bekommt `page: "/admin/mail-settings"` und verschwindet damit aus den Settings-Tabs (Mechanik aus Paket 1).

**Tech Stack:** .NET 10, MailKit (vorhanden), HttpClient, Blazor WASM, MudBlazor, xunit/bUnit.

**Spec:** `docs/superpowers/specs/2026-10-13-settings-cleanup-and-extensions-design.md` (Paket 6)

## Global Constraints

- Branch `feat/mail-providers` von `main`; Merge nach `main` wenn grün.
- `IMailSender.QueueAsync(...)` unverändert; Queue, Templates, Retry, Whitelist bleiben.
- `ISmtpTransport` → `IMailTransport` ohne Obsolete-Shim. Bestehende Keys `Mail.Smtp.*`, `Mail.From`, `Mail.Whitelist` bleiben unverändert (keine Datenmigration).
- Neue Keys: `Mail.Provider` (Choice `smtp|sendGrid|mailgun|mailchimp`, Default `smtp`, Global+Tenant), `Mail.ApiKey` (Secret, Global+Tenant; SendGrid/Mailgun/Mailchimp), `Mail.Mailgun.Domain` (String), `Mail.Mailgun.Region` (Choice `us|eu`, Default `us`), `Mail.UseOwnServer` (Bool, **nur Tenant**, Default `false`).
- Tenant-Override: `Mail.UseOwnServer` an → **alle** Mail-Werte (Anbieter, Zugangsdaten, From, Whitelist) aus Tenant-Scope; aus → alle aus Global. Ausschalten löscht nichts. System-Organisation nutzt immer Global.
- Endpunkte der Anbieter: SendGrid `POST https://api.sendgrid.com/v3/mail/send` (Bearer); Mailgun `POST https://api.mailgun.net/v3/{domain}/messages` bzw. EU `https://api.eu.mailgun.net/v3/{domain}/messages` (Basic `api:{key}`, form-urlencoded `from,to,subject,html`); Mailchimp Transactional `POST https://mandrillapp.com/api/1.0/messages/send` (JSON `{ key, message: { html, subject, from_email, to: [{ email, type: "to" }] } }`, Antwort-Status `rejected`/`invalid` = Fehler).
- CCD, `.razor` + `.razor.cs`, keine erklärenden Kommentare außer nicht offensichtlichem Warum; keine Attribution; neue Texte EN-Key + `de.json`; Docs EN + DE.

## Review Focus

- Secrets nie im Klartext an den Client; Test mit ungespeichertem Entwurf nutzt gespeicherten Key, wenn das Key-Feld leer ist (wie AI-Seite).
- Fehlerantworten der HTTP-Anbieter (4xx/5xx, Mandrill `rejected`) → Exception mit verständlicher Meldung inkl. Status, damit Retry/LastError greifen; kein Secret in der Meldung.
- Organisation mit `UseOwnServer=true`, aber unvollständiger Konfiguration → Fehler „nicht konfiguriert“, **kein** stilles Zurückfallen auf Global.
- Hintergrundjob läuft im Tenant-Kontext der Mail (`JobEnvelope`) – Resolver nutzt diesen Tenant, nicht den des Admins.
- Unbekannter `Mail.Provider`-Wert (z. B. eigener Transport entfernt) → klarer Fehler.

---

### Task 1: Transporte, Settings, Auflösung

**Interfaces:**
- Produces (public, `Coworkee.Mailing`):
  - `public sealed record MailEnvelope(string From, string To, string Subject, string HtmlBody);`
  - `public sealed record MailConnection(string Provider, IReadOnlyDictionary<string, string?> Values) { public string? this[string name] { get; } }`
  - `public interface IMailTransport { string Provider { get; } Task SendAsync(MailEnvelope mail, MailConnection connection, CancellationToken cancellationToken); }`
  - `public static class MailProviders { Smtp = "smtp", SendGrid = "sendGrid", Mailgun = "mailgun", Mailchimp = "mailchimp"; All }` (Contracts, damit der Client sie nutzt)
  - neue Konstanten in `MailSettings` (`Provider`, `ApiKey`, `MailgunDomain`, `MailgunRegion`, `UseOwnServer`) – in Contracts verschieben, falls der Client sie braucht (eine Quelle).
  - `internal sealed class MailConnectionResolver` → `Task<MailConnection> ResolveAsync(CancellationToken)` (wirft `InvalidOperationException` mit „… is not configured.“ bei fehlenden Pflichtwerten des Anbieters).
- Settings scope-genau lesen: falls `ISettingProvider` keinen Scope-Lesezugriff hat, minimal ergänzen (z. B. `GetForScopeAsync(name, scope, ct)`), konsistent mit Cache/Entschlüsselung.

**Files:**
- Modify: `src/Coworkee.Mailing/MailDelivery.cs` (SmtpSettings/ISmtpTransport ersetzen; `SendMailJob` nutzt Resolver + Transport nach `Provider`; Fehlermeldung bei unbekanntem Anbieter)
- Create: `src/Coworkee.Mailing/Transports/SmtpMailTransport.cs`, `SendGridMailTransport.cs`, `MailgunMailTransport.cs`, `MailchimpMailTransport.cs`, `src/Coworkee.Mailing/MailConnectionResolver.cs`
- Modify: `src/Coworkee.Mailing/CoworkeeMailingModule.cs` (Definitionen inkl. `page: "/admin/mail-settings"`, `enabledWhen` je Anbieter, Registrierung `TryAddEnumerable` der Transporte, `AddHttpClient`)
- Modify: `MailSetupCheck` (anbieterabhängig), alle Verwender von `ISmtpTransport`/`SmtpSettings` (grep src, tests, templates)
- Test: `tests/Coworkee.Mailing.Tests` (Projekt suchen): Request-Aufbau je HTTP-Anbieter gegen Fake-`HttpMessageHandler` (URL, Auth-Header, Body), Fehlerfall 4xx, Mandrill `rejected`; Resolver: Global, Tenant mit/ohne `UseOwnServer`, unvollständig → Fehler; Job mit Fake-Transport schickt über gewählten Anbieter.

- [ ] Failing tests → RED → implementieren → GREEN (Mailing-, Settings-, Aspire-Tests, Template-Build falls betroffen)
- [ ] Commit `feat(mail): SendGrid, Mailgun and Mailchimp next to SMTP; organisations may use their own server`

### Task 2: Seite `/admin/mail-settings` mit Verbindungstest

**Interfaces:**
- Consumes: Task 1 (`MailProviders`, Setting-Namen, `MailConnection`, `IMailTransport`).
- Produces: Endpoint `POST /api/v1/mail/connection/test` mit Body `MailConnectionTestRequest(IReadOnlyDictionary<string, string?> Values)` (Entwurf; leeres Secret → gespeicherter Wert des aktuellen Scopes), sendet sofort (ohne Queue, ohne Whitelist) an die Adresse des aktuellen Benutzers, Antwort `MailConnectionTestDto(bool Success, string? Error)`; Permission `Settings.Manage`. Client-API-Methode im bestehenden Muster (`ICoworkeeApi`/eigene API wie `IAiConnectionApi`).

**Files:**
- Create: `src/Coworkee.Client.Blazor/Pages/Admin/MailConfiguration.razor` + `.razor.cs` (Route `/admin/mail-settings`, Aufbau/Optik analog `AiConfiguration.razor`: Anbieterwahl, nur passende Felder, From, Whitelist, Buttons „Discard changes“, „Send test mail“, „Save“; in einer Organisation ≠ System: Schalter „Own mail server“ – aus → Felder ausgeblendet und Hinweis „Mails go through the system's server.“; System: Global-Scope).
- Modify: Navigation (`CoworkeeClientExtensions.cs` neben AI-Seite), Mail-Modul-Endpoint, Client-API.
- Test: bUnit `MailConfigurationTests` (Anbieterwechsel zeigt passende Felder; Speichern sendet nur Mail-Keys des Scopes; Test-Button ruft Test-API mit Entwurf; Organisation ohne eigenen Server zeigt Hinweis), Endpoint-Test im Mailing-Testprojekt (Entwurf mit leerem Secret nutzt gespeicherten Key; Fake-Transport).

- [ ] Failing tests → RED → implementieren → GREEN
- [ ] Commit `feat(mail): mail settings page with provider choice and a test mail`

### Task 3: Docs, Demo, Merge

- [ ] Docs Mailing-Modul (EN + DE): Anbieter, Keys, Tenant-Override, eigener `IMailTransport` registrieren (Beispiel), Seite. mkdocs strict.
- [ ] Merge, Pack, Demo: Admin → Mail-Einstellungen, Testmail an Mailpit; Settings-Tabs ohne Mail-Gruppe.
