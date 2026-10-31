# Response-Filter

Ein Response-Filter ändert, was ein Endpunkt zurückgibt, abhängig davon, wer fragt, ohne den Handler anzufassen. Coworkee bindet [Nextended.ResponseFilters](https://github.com/fgilde/Nextended) für Minimal-APIs und MVC an.

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

`AddCoworkeeResponseFilters` findet alle Filter in den angegebenen Assemblies und wendet sie auf jede mit `MapCoworkeeApi` angelegte Gruppe und auf MVC-Ergebnisse an. Antworten, die außerhalb eines Endpunkt-Ergebnisses geschrieben werden, bekommen dieselbe Form: die Treffer des Streams der globalen Suche (`IResponseShaper`) und die Ergebnisse, die die Werkzeuge des Assistenten und von MCP an das Modell geben, damit eine Eigenschaft, die ein Filter ausblendet, nicht über sie durchsickert. Das optionale zweite Argument konfiguriert die `ResponseFilterOptions` von Nextended.

## Regeln

| Regel | Wirkung |
|---|---|
| `Remove(x => x.Token)` | die Eigenschaft verschwindet aus dem JSON |
| `Nullify(x => x.Salary)` | die Eigenschaft wird als `null` gesendet |
| `Mask(x => x.Card).KeepFirst(4).KeepLast(4)` | `4111********1111` |
| `Hash(x => x.Email).AsSha256()` | ein stabiler Hash statt des Werts |

Jede Regel endet mit einer Bedingung: `.Always()`, `.When(...)`, `.Unless(...)` oder den Coworkee-Kurzformen `.WhenGranted(permission)` und `.UnlessGranted(permission)`.

!!! info "OData"
    OData serialisiert seine Ergebnisse selbst. Die Filter lassen sie aus; blende interne Eigenschaften im Modell aus (`AddODataEntity<T>(set, permission, x => x.Secret)`) und schränke Zeilen mit einem [Entity-Filter](odata.md#regeln-pro-zeile) ein.
