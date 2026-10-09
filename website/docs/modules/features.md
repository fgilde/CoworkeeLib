# Features and editions

`Coworkee.Features` switches parts of the app on and off per tenant. Modules define features, editions bundle feature values, and every tenant gets an edition plus its own overrides. The package also brings the tenant administration of the host.

```csharp
[DependsOn(typeof(CoworkeeFeaturesModule))]
public sealed class AppModule : CoworkeeModule;
```

## Defining features

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

Types are `Bool` (default `false`), `Int` and `String`. A tenant's value is resolved in this order: its own override, the value of its edition, the default of the definition. Without a tenant the defaults apply.

## Checking features

```csharp
if (await features.IsEnabledAsync("Reports.Enabled", ct)) { ... }
var max = await features.GetValueAsync("Reports.MaxPerMonth", ct);
```

`IFeatureChecker` reads through HybridCache; saving an edition or the features of a tenant clears the cache at once.

Commands and queries declare what they need, like permissions; the dispatcher answers `403` with the code `feature.disabled` while the feature is off:

```csharp
[RequiresFeature("Reports.Enabled")]
public sealed record CreateReport(string Name) : ICommand<Result<Guid>>;
```

Minimal APIs outside the dispatcher use the endpoint filter:

```csharp
app.MapGet("/api/v1/reports/export", Export).RequireFeature("Reports.Enabled");
```

## Blazor

`FeatureStore` loads the features of the current tenant once and reloads them when the server reports a change over realtime (topic `global:features`).

```razor
<FeatureGate Feature="Reports.Enabled">
    <ChildContent><ReportList /></ChildContent>
    <Disabled>Reports are part of the Pro edition.</Disabled>
</FeatureGate>
```

Navigation items hide while their feature is off:

```csharp
new CoworkeeNavItem("Reports", "/reports", Icons.Material.Outlined.Assessment, Feature: "Reports.Enabled")
```

## Administration

Both pages belong to the system organisation: every admin holds the permissions, the server also checks that the user belongs to the host.

- *Administration > Tenants* (`Features.Tenants`): all tenants with their user count; create (optionally with a first administrator) and edit, activate and deactivate, assign an edition, override single features.
- *Administration > Editions* (`Features.Editions`): create, rename and delete editions and set their feature values. Deleting an edition puts its tenants back on the defaults.

| Endpoint | Purpose |
|---|---|
| `GET /api/v1/features` | values of the current tenant |
| `GET /api/v1/features/definitions` | all definitions, grouped |
| `GET/POST/PUT/DELETE /api/v1/editions` | editions |
| `POST /api/v1/tenants`, `PUT /api/v1/tenants/{id}` | create and change tenants |
| `POST /api/v1/tenants/details` | edition, overrides and user count per tenant |
| `PUT /api/v1/tenants/{id}/features` | edition and overrides of a tenant |
| OData `Tenants` | the tenant table |

## Database

The module adds `cw.Editions` (values as `jsonb`) and `cw.TenantFeatures` (edition and overrides per tenant). Create a migration after adding the module.
