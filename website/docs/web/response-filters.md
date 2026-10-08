# Response filters

A response filter changes what an endpoint returns, depending on who asks, without touching the handler. Coworkee wraps [Nextended.ResponseFilters](https://github.com/fgilde/Nextended) for minimal APIs and MVC.

```csharp title="MyApp.Catalog/ResponseFilters/DashboardResponseFilter.cs"
using Coworkee.ResponseFilters;
using Nextended.ResponseFilters;

internal sealed class DashboardResponseFilter : ResponseFilter<DashboardDto>
{
    public DashboardResponseFilter()
    {
        Nullify(d => d.Users).UnlessGranted(IdentityPermissions.Users.View);
        Nullify(d => d.Roles).UnlessGranted(IdentityPermissions.Roles.View);
    }
}
```

```csharp title="MyApp.Api/MyAppApiModule.cs"
context.Services.AddCoworkeeResponseFilters([typeof(MyAppCatalogModule).Assembly]);
```

`AddCoworkeeResponseFilters` finds all filters in the given assemblies and applies them to every group created with `MapCoworkeeApi` and to MVC results.

## Rules

| Rule | Effect |
|---|---|
| `Remove(x => x.Token)` | the property disappears from the JSON |
| `Nullify(x => x.Salary)` | the property is sent as `null` |
| `Mask(x => x.Card).KeepFirst(4).KeepLast(4)` | `4111********1111` |
| `Hash(x => x.Email).AsSha256()` | a stable hash instead of the value |

Each rule ends with a condition: `.Always()`, `.When(...)`, `.Unless(...)`, or the Coworkee shortcuts `.WhenGranted(permission)` and `.UnlessGranted(permission)`.

!!! info "OData"
    OData serializes its own results. Filters skip them; hide internal properties in the model instead (`AddODataEntity<T>(set, permission, x => x.Secret)`), and narrow rows with an [entity filter](odata.md#row-level-rules).
