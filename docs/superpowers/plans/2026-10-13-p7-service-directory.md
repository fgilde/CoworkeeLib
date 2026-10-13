# Paket 7 – Service-Verzeichnis mit AspireUI: Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ein öffentlicher Backend-Service `IServiceDirectory` liefert alle bekannten Dienste (konfigurierte + die von einer AspireUI gehosteten Apps/Stacks), Apps können Dienste per Name auflösen; die Admin-Seite „Dienste“ nutzt denselben Service; eine AspireUI mit `WithRetrieveApi` wird in Aspire automatisch erkannt.

**Architecture:** Quellen über `IServiceSource` (erweiterbar): `ConfiguredServiceSource` (heutiges `Coworkee:Services`) und `AspireUIServiceSource` (`GET {Url}/api/retrieve/apps`, Header `X-Api-Key` falls Key gesetzt; Antwort `[{ name, kind, healthy, urls }]`). `ServiceDirectory` führt zusammen, prüft Health der konfigurierten Dienste wie heute, cached die AspireUI-Liste kurz. Aspire-Integration erkennt `AspireUIResource` mit `ASPIREUI_RETRIEVE_API=true` und gibt der App `Coworkee:AspireUI:Url` (+ `ApiKey`) mit.

**Tech Stack:** .NET 10, Aspire 13 (Hosting), Nextended.Aspire.Hosting.AspireUI 10.1.45 (`AspireUIResource`, `AspireUIRetrieveApiExtensions.Path/KeyHeader`), HttpClient, Blazor.

**Spec:** `docs/superpowers/specs/2026-10-13-settings-cleanup-and-extensions-design.md` (Paket 7)

## Global Constraints

- Branch `feat/service-directory` von `main`; Merge nach `main` wenn grün.
- Nextended-Paket(e) auf `10.1.45` heben (`Directory.Packages.props:60` u. ä.).
- Optionen: `public sealed class CoworkeeAspireUIOptions { const string Section = "Coworkee:AspireUI"; string? Url; string? ApiKey; }` in `src/Coworkee.Contracts/Configuration`. Ohne `Url` ist die AspireUI-Quelle inaktiv.
- Timeout je Abfrage 3 s (wie bestehender Health-Probe), AspireUI-Liste 15 s gecacht; Fehler/Timeout/401/404 → Quelle liefert leer, Warn-Log (ohne Key), Seite zeigt Hinweis je Quelle.
- `ServiceDto` erweitern ohne bestehende Konstruktoraufrufe zu brechen (optionale Parameter am Ende): `string Source = "app"`, `IReadOnlyList<string>? Urls = null`; AspireUI-Einträge: `Source = "aspireui"`, `Url` = erste URL (leer erlaubt), Health aus `healthy`.
- Namenskonflikt: konfigurierter Dienst gewinnt bei `ResolveAsync`; in der Liste erscheinen beide.
- Nur System-Organisation darf die Liste per API sehen (wie heute); `IServiceDirectory` selbst hat keine Benutzerprüfung (Backend-Code).
- CCD, keine erklärenden Kommentare außer nicht offensichtlichem Warum; keine Attribution; Texte EN + `de.json`; Docs EN + DE.

## Review Focus

- AspireUI nicht erreichbar → Admin-Seite lädt trotzdem schnell (paralleler Abruf, Timeout), konfigurierte Dienste erscheinen.
- `ApiKey` taucht nie in Logs, DTOs oder Client-Antworten auf.
- Aspire: `WithRetrieveApi(parameter)` – Key als Parameter-Referenz an die App weitergeben, nicht als aufgelöster String im Manifest; `WithRetrieveApi()` ohne Key → kein ApiKey-Env.
- Aspire-Publish-Mode: nichts injizieren (wie `WireServices`).
- Mehrere Abrufe gleichzeitig (Seite pollt alle paar Sekunden) → ein HTTP-Call je Cache-Periode.

---

### Task 1: `IServiceDirectory` + Quellen (Backend)

**Interfaces:**
- Produces (public, Namespace `Coworkee.Settings`):
  - `public interface IServiceSource { string Name { get; } Task<IReadOnlyList<ServiceDto>> GetAsync(CancellationToken cancellationToken); }`
  - `public interface IServiceDirectory { Task<IReadOnlyList<ServiceDto>> GetAllAsync(CancellationToken cancellationToken); Task<ServiceDto?> ResolveAsync(string name, CancellationToken cancellationToken); }` – `ResolveAsync` ohne Health-Probe (schnell), case-insensitive Name.
  - Registrierung: `TryAddEnumerable` für Quellen, `TryAddScoped`/Singleton für Directory – Apps registrieren eigene `IServiceSource`.
- `GetServices`-Handler (`src/Coworkee.Settings/ServiceDirectory.cs`) nutzt `IServiceDirectory`; Systemorganisations-Prüfung bleibt.

**Files:**
- Modify: `src/Coworkee.Settings/ServiceDirectory.cs` (aufteilen: `ServiceDirectory.cs`, `ConfiguredServiceSource.cs`, `AspireUIServiceSource.cs`), `src/Coworkee.Contracts/Settings/ServicesContracts.cs`, `src/Coworkee.Contracts/Configuration/CoworkeeAspireUIOptions.cs` (neu), Options-Bindung neben `CoworkeeServicesOptions` (`CoworkeeWebApplicationExtensions.cs:33`), Modul-Registrierung.
- Test: `tests/Coworkee.Settings.Tests` (Fake-`HttpMessageHandler`): AspireUI-Liste wird gemappt (Name, Urls, Health, Source), Key-Header gesetzt/nicht gesetzt, 401/Timeout → leer, Cache (zwei Aufrufe → ein Request), Merge + `ResolveAsync` (konfiguriert gewinnt), API-Endpoint liefert beide Quellen.

- [ ] Failing tests → RED → implementieren → GREEN
- [ ] Commit `feat(settings): service directory with pluggable sources, AspireUI apps included`

### Task 2: Aspire-Erkennung

**Files:**
- Modify: `src/Coworkee.Aspire/CoworkeeApp.Services.cs` (im Run-Mode: für jede `AspireUIResource`, deren Umgebung `ASPIREUI_RETRIEVE_API=true` setzt, `Coworkee__AspireUI__Url` = deren http-Endpoint und – falls `ASPIREUI_RETRIEVE_API_KEY` gesetzt – `Coworkee__AspireUI__ApiKey` = derselbe Wert/Parameter; Werte über die Environment-Callbacks der Ressource ermitteln, z. B. Aspire-API zum Auswerten der Umgebung oder eigenes Ausführen der `EnvironmentCallbackAnnotation`s in einem Kontext). Mehrere AspireUIs: erste gewinnt (Report).
- Modify: `Directory.Packages.props` (Nextended 10.1.45).
- Test: `tests/Coworkee.Aspire.Tests` (vgl. `ToolsTests.cs:115-133` für AspireUI-Setup): mit `WithRetrieveApi()` → Url-Env, kein Key; mit `WithRetrieveApi(param)` → Key-Env verweist auf Parameter; ohne `WithRetrieveApi` → nichts; Publish-Mode → nichts.

- [ ] Failing tests → RED → implementieren → GREEN
- [ ] Commit `feat(aspire): an AspireUI with the retrieve API is passed to the app's service directory`

### Task 3: Services-Seite + Docs

**Files:**
- Modify: `src/Coworkee.Client.Blazor/Pages/Admin/Services.razor(.cs)` (Gruppierung nach `Source`: „App“ / „AspireUI“, mehrere URLs als Links, Hinweis wenn eine Quelle nicht erreichbar war – dafür Quelle-Status im DTO/Antwort ergänzen, falls nötig minimal), `de.json`.
- Docs: `website/docs/modules/settings.md` (Abschnitt Services) + `hosting/aspire.md` (EN + DE): `IServiceDirectory` injizieren (Beispiel `ResolveAsync("billing")`), Quellen, eigene `IServiceSource`, AspireUI-Einrichtung (`builder.AddAspireUI()...WithRetrieveApi()` → automatisch; ohne Aspire `Coworkee:AspireUI:Url/ApiKey`), Hinweis, dass AspireUI die *selbst gehosteten* Apps/Stacks liefert.
- Test: bUnit Services-Seite (zwei Gruppen, Links).

- [ ] Failing tests → RED → implementieren → GREEN, mkdocs strict
- [ ] Commit `feat(ui): services page groups by source`
- [ ] Merge, Pack, Demo prüfen
