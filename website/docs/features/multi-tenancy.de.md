---
title: "Mandantenfähige SaaS-Basis für .NET"
description: "Mandantenfähigkeit für ASP.NET Core und Blazor: Mandantenfilter in EF Core, Einstellungen pro Installation, Mandant und Benutzer, typisierte Konfiguration, Editionen."
---

# Mandanten, Einstellungen und Editionen

```csharp
public sealed class Brand : AuditedEntity, IMultiTenant
{
    public Guid TenantId { get; set; }
    public required string Name { get; set; }
}
```

Ein Marker-Interface, mehr braucht es nicht. Ab jetzt liefert `db.Set<Brand>()` nur noch die Zeilen der Organisation des Aufrufers, denn `CoworkeeDbContext` legt auf jede `IMultiTenant`-Entität einen benannten Query-Filter für den aktuellen Mandanten. Beim Einfügen wird `TenantId` aus dem aktuellen Benutzer gesetzt, und wer eine Zeile in einen fremden Mandanten speichern will, bekommt eine Exception.

Entitäten aus `coworkee entity add` tragen den Marker von Haus aus, und ihre eindeutigen Indizes gelten pro Mandant. Bei den Marken im Template gilt das für den Namen: Zwei Organisationen dürfen also beide eine Marke „Acme“ haben.

`ICurrentUser` kennt Benutzer, Mandant und Berechtigungen des Requests. Hintergrundjobs und Outbox stellen Benutzer und Mandanten wieder her, die sie angestoßen haben. Muss Code im Namen eines anderen handeln, etwa ein Seed, öffnet er einen Scope:

```csharp
using (CurrentUserScope.Begin(new ImpersonatedUser(adminId, tenantId)))
{
    // queries and inserts run as this user and tenant
}
```

Gecachte Queries teilen sich ihre Einträge standardmäßig pro Mandant (`CacheScope.Tenant`).

## Organisationen und der Host

Ein Mandant ist eine Organisation mit Namen, Kennung und Aktiv-Status. Eine davon ist die Systemorganisation, die die Installation verwaltet: Ihre Administratoren legen unter *Administration > Mandanten* Organisationen an, auf Wunsch gleich mit einem ersten Administrator, und deaktivieren sie. Mit einer deaktivierten Organisation sind auch die API-Schlüssel ihrer Benutzer tot.

Berechtigungen wie `Features.Tenants` und `Identity.Clients.Manage` gehören dem Host. Jeder Administrator hat sie, nutzen können sie aber nur die Administratoren der Systemorganisation.

## Einstellungen auf drei Ebenen

```csharp
context.Group("Billing", "Billing")
    .Add("Billing.Currency", "Currency", SettingType.Choice, [SettingScope.Tenant], "EUR", choices: ["EUR", "USD", "CHF"])
    .Add("Billing.ApiKey", "API key", SettingType.Secret, [SettingScope.Global]);
```

Es gibt die Ebenen `Global`, `Tenant` und `User`, und der spezifischste Wert gewinnt: Eine Organisation kann die Standardsprache der Installation überschreiben, ein Benutzer beide. Sobald der `ISettingDefinitionContributor` registriert ist, erscheint die Einstellung auf der Admin-Seite im Reiter ihrer Ebene, ganz ohne Formularcode. Geheimnisse werden mit ASP.NET Core Data Protection verschlüsselt und landen nie im Browser.

![Einstellungsseite mit den Reitern App-Einstellungen, System und Organisation](../assets/screenshots/app-configuration-light.webp){ .shot loading=lazy }

Größere Konfigurationsblöcke beschreibst du stattdessen als typisierte Klasse. Die Werte kommen aus appsettings; was Administratoren in der Anwendung ändern, liegt in der Datenbank und wird über `IOptionsMonitor<T>` gelesen. Regeln legen fest, was der Browser anfassen darf:

```csharp
context.Services.AddCoworkeeSettings<MyAppSettings>(context.Configuration, rules => rules
    .Lock(s => s.Billing.Currency)          // shown disabled, never changed
    .Protect(s => s.Billing.WebhookUrl)     // editable once its lock button is pressed
    .Hide(s => s.Billing.WebhookSecret));   // never leaves the server
```

**Stand:** Version 0.0.1.x. Typisierte App-Einstellungen gelten nur für die ganze Installation (Werte pro Mandant sind normale Einstellungen), und MySQL wird nicht unterstützt, weil es dafür noch keinen EF-Core-10-Provider gibt.

## Features und Editionen

`Coworkee.Features` schaltet Teile der Anwendung pro Mandant an und aus. Module definieren Features (`Bool`, `Int` oder `String`), Editionen bündeln Werte, und jeder Mandant bekommt eine Edition und dazu eigene Überschreibungen. Erst zählt die Überschreibung, dann die Edition, dann der Standardwert.

```csharp
[RequiresFeature("Reports.Enabled")]
public sealed record CreateReport(string Name) : ICommand<Result<Guid>>;
```

Solange das Feature aus ist, antwortet der Dispatcher mit `403` und `feature.disabled`. In Blazor blenden `FeatureGate` und Navigationseinträge mit `Feature` aus, was nicht zur Edition gehört, und offene Clients bekommen Änderungen über Realtime mit.

Referenz: [Persistenz](../fundamentals/persistence.md#mandanten-und-aktueller-benutzer), [Einstellungen und App-Konfiguration](../modules/settings.md), [Features und Editionen](../modules/features.md), [Berechtigungen nur für den Host](../security/permissions.md#berechtigungen-nur-fur-den-host).
