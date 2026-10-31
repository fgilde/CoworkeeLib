# Query builder

`QueryBuilder` builds a filter visually: nested *all / any* groups of conditions, each with a field, an operator that fits the field's type and a value editor of that type. It knows nothing about where data lives. The query is a plain model that translates to OData `$filter` text and to a LINQ expression, and that saves as JSON.

The component, its model and both translators live in `Coworkee.Client.Blazor.Components.Query` and use only MudBlazor, so they work in any Blazor app. The [data table](data-table.md#advanced-filter) uses it as its *Advanced filter*.

![Query builder](../assets/screenshots/advanced-filter.png){ .shot }

```razor
<QueryBuilder Fields="Fields" @bind-Value="_query" Localize="text => L[text]" />

@code {
    private QueryGroup _query = new();

    private static readonly QueryField[] Fields =
    [
        new("Number", "Number", QueryFieldType.Text),
        new("Status", "Status", QueryFieldType.Enum) { Options = [new("Open", "Open"), new("Shipped", "On the way")] },
        new("Total", "Total", QueryFieldType.Number),
        new("OrderDate", "Order date", QueryFieldType.DateTime),
        new("Paid", "Paid", QueryFieldType.Boolean),
        new("Customer.Name", "Customer", QueryFieldType.Text),
        new("CustomerId", "Customer", QueryFieldType.Guid) { Lookup = SearchCustomersAsync },
    ];

    private string? Filter => ODataQueryTranslator.ToFilter(_query, Fields);
}
```

| Parameter | |
|---|---|
| `Fields` | the fields to offer |
| `Value`, `ValueChanged` | the query; the builder changes it in place and raises `ValueChanged` after each edit |
| `Localize` | translates the builder's English texts and operator names |
| `ShowPreview` | the preview as text, OData and JSON (default `true`) |
| `ShowImportExport` | JSON import and download (default `true`) |
| `MaxDepth` | how deep groups nest (3; 0 allows conditions only) |
| `Dense` | compact inputs (default `true`) |
| `Now` | "now" for relative dates; defaults to the local clock |

## Fields

A `QueryField` has a name (the property path, `Customer.Name` or `Customer/Name`), a label and a `QueryFieldType`:

| Type | Operators | Value editor |
|---|---|---|
| `Text` | contains, does not contain, equals, does not equal, starts with, ends with, is any of, is none of, is empty, is not empty | text; a list of entries for *is any of* |
| `Number` | =, &ne;, >, &ge;, <, &le;, between, not between, is any of, is none of | number |
| `Boolean` | is yes, is no | |
| `Date`, `DateTime` | is on, is not on, before, after, on or before, on or after, between, not between, today, this week, this month, this year, in the last n days, in the next n days | date picker, number of days |
| `Enum` | is any of, is none of, is, is not | choice of the `Options` |
| `Guid` | is, is not, is any of, is none of | choice of `Options`, search through `Lookup`, else text |

`Options` turn any field into a choice, `Lookup` searches choices as the user types (`Func<string?, CancellationToken, Task<IReadOnlyList<QueryOption>>>`); a rule remembers the labels of looked up values. `Flags = true` marks a `[Flags]` enum: *is* and *is any of* test single flags (`Options has 'Express'`). `Nullable = true` adds *is empty* and *is not empty*, `Operators` narrows the list, `Category` groups the field picker.

## Translating

```csharp
string? filter = ODataQueryTranslator.ToFilter(query, fields);              // OData $filter, null when nothing filters
Expression<Func<Order, bool>> predicate = LinqQueryTranslator.ToExpression<Order>(query, fields);
IQueryable<Order> rows = db.Orders.Where(query, fields);                     // EF Core translates it
IEnumerable<Order> local = orders.Where(query, fields);                      // in memory
string text = QueryDescriber.Describe(query, fields, l);                     // "Status is any of Open, On the way and Total is at least 100"
```

Both translators read a query the same way:

- text compares ignoring case (`contains(tolower(Name),'drill')`, `Name.ToLower().Contains("drill")`) and null text never matches;
- dates compare by whole days in the offset of `now` (the optional last argument of each translator, the local clock by default): *is on 11 Oct* is `ge 2026-10-11T00:00:00+02:00 and lt 2026-10-12T00:00:00+02:00`, *this week* starts on Monday;
- enums compare by member name (`Status eq 'Shipped'`), so saved queries survive reordered enums;
- a null navigation reads as null (`Customer.Name` of an order without customer);
- incomplete conditions and empty groups are left out, so a query being edited is always valid;
- quotes in values are doubled (`'O''Neil'`).

The tests run every operator through both translators and through the OData engine of [client entities](client-entities.md) and expect the same rows.

## Saving

```csharp
string json = QueryJson.Serialize(query);
QueryGroup? back = QueryJson.Deserialize(json);   // null for text that is no query
```

JSON uses names for enums and leaves empty values out:

```json
{"logic":"Or","children":[{"kind":"rule","field":"Status","operator":"In","values":["Open","Shipped"]},
 {"kind":"group","logic":"And","children":[{"kind":"rule","field":"Total","operator":"GreaterThanOrEqual","value":"100"}]}]}
```

Values are invariant text: numbers with a dot, dates as `yyyy-MM-dd`, enums by member name, so a query reads the same in every language. The preview's buttons download the query and load one from pasted JSON.

## Model

| Type | |
|---|---|
| `QueryGroup` | `Logic` (`And`, `Or`), `Not`, `Children`; `Rules()` walks all rules, `IsEmpty` |
| `QueryRule` | `Field`, `Operator`, `Value`, `Value2` (upper bound of *between*), `Values` (*is any of*), `Labels` |
| `QueryOperators` | `For(field)`, `NeedsValue`, `IsList`, `IsRange`, `IsComplete`, `Label(op, type)` |
| `QueryDates` | the day range of a date rule |

The pieces render alone too: `QueryGroupEditor`, `QueryRuleEditor` and `QueryValueEditor` for a custom layout.
