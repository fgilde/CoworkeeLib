# Features und Editionen

`Coworkee.Features` schaltet Teile der App pro Mandant ein und aus. Module definieren Features, Editionen bündeln Feature-Werte, und jeder Mandant erhält eine Edition und eigene Überschreibungen. Das Paket bringt außerdem die Mandantenverwaltung des Hosts mit.

```csharp
[DependsOn(typeof(CoworkeeFeaturesModule))]
public sealed class AppModule : CoworkeeModule;
```

## Features definieren

```csharp
internal sealed class ReportFeatures : IFeatureDefinitionContributor
{
    public void Define(FeatureDefinitionContext context) =>
        context.Group("Reports", "Reports")
            .Add("Reports.Enabled", "Reports")
            .Add("Reports.MaxPerMonth", "Reports per month", FeatureType.Int, "10");
}

services.AddSingleton<IFeatureDefinitionContributor, ReportFeatures>();
```

Typen sind `Bool` (Standard `false`), `Int` und `String`. Der Wert eines Mandanten ergibt sich in dieser Reihenfolge: seine eigene Überschreibung, der Wert seiner Edition, der Standardwert der Definition. Ohne Mandant gelten die Standardwerte.

## Features prüfen

```csharp
if (await features.IsEnabledAsync("Reports.Enabled", ct)) { ... }
var max = await features.GetValueAsync("Reports.MaxPerMonth", ct);
```

`IFeatureChecker` liest über HybridCache; das Speichern einer Edition oder der Features eines Mandanten leert den Cache sofort.

Commands und Queries geben wie bei Berechtigungen an, was sie brauchen; der Dispatcher antwortet mit `403` und dem Code `feature.disabled`, solange das Feature aus ist:

```csharp
[RequiresFeature("Reports.Enabled")]
public sealed record CreateReport(string Name) : ICommand<Result<Guid>>;
```

Minimal APIs außerhalb des Dispatchers nutzen den Endpoint-Filter:

```csharp
app.MapGet("/api/v1/reports/export", Export).RequireFeature("Reports.Enabled");
```

## Blazor

`FeatureStore` lädt die Features des aktuellen Mandanten einmal und lädt sie neu, sobald der Server per Realtime eine Änderung meldet (Topic `global:features`).

```razor
<FeatureGate Feature="Reports.Enabled">
    <ChildContent><ReportList /></ChildContent>
    <Disabled>Reports gibt es in der Pro-Edition.</Disabled>
</FeatureGate>
```

Navigationseinträge verschwinden, solange ihr Feature aus ist:

```csharp
new CoworkeeNavItem("Reports", "/reports", Icons.Material.Outlined.Assessment, Feature: "Reports.Enabled")
```

## Verwaltung

Beide Seiten gehören zur Systemorganisation: Jeder Administrator hat die Berechtigungen, der Server prüft zusätzlich, ob der Benutzer zum Host gehört.

- *Administration > Mandanten* (`Features.Tenants`): alle Mandanten mit Benutzeranzahl; anlegen (optional mit einem ersten Administrator) und bearbeiten, aktivieren und deaktivieren, eine Edition zuweisen, einzelne Features überschreiben.
- *Administration > Editionen* (`Features.Editions`): Editionen anlegen, umbenennen und löschen und ihre Feature-Werte festlegen. Wird eine Edition gelöscht, gelten für ihre Mandanten wieder die Standardwerte.

| Endpoint | Zweck |
|---|---|
| `GET /api/v1/features` | Werte des aktuellen Mandanten |
| `GET /api/v1/features/definitions` | alle Definitionen, gruppiert |
| `GET/POST/PUT/DELETE /api/v1/editions` | Editionen |
| `POST /api/v1/tenants`, `PUT /api/v1/tenants/{id}` | Mandanten anlegen und ändern |
| `POST /api/v1/tenants/details` | Edition, Überschreibungen und Benutzeranzahl je Mandant |
| `PUT /api/v1/tenants/{id}/features` | Edition und Überschreibungen eines Mandanten |
| OData `Tenants` | die Mandantentabelle |

## Datenbank

Das Modul ergänzt `cw.Editions` (Werte als `jsonb`) und `cw.TenantFeatures` (Edition und Überschreibungen je Mandant). Nach dem Hinzufügen des Moduls eine Migration erzeugen.
