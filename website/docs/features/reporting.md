---
title: "Dashboards and reporting for Blazor apps"
description: "Self-service dashboards for Blazor and ASP.NET Core: a drag and drop designer, KPIs, charts and pivots over your OData sets, cross filters and SQL-side aggregation."
---

# Dashboards and reporting in your Blazor app

Sooner or later every business app gets the question "how many orders did we ship per region last quarter?". The usual answers are an export to a spreadsheet, a separate BI tool that needs its own copy of the data and its own permission model, or a developer writing one more hand-made chart page.

Coworkee's reporting module puts dashboards into the app itself. Users arrange KPIs, charts, pivots and tables on a grid, filter them together and share the result with roles or the whole organisation. The data comes from the OData entity sets you already registered, with the same permissions and row filters as the rest of the app, and every number is aggregated by the database.

![A sales dashboard with KPIs, charts, a pivot and a table](../assets/screenshots/reporting-dashboard.png){ .shot loading=lazy }

## Setup

The server module brings the tables, endpoints, permissions and live updates. The client registration adds the pages under `/reports` and a navigation entry, and names the pages that drill-down should land on:

```csharp
[DependsOn(typeof(CoworkeeReportingModule))]
public sealed class MyAppDatabaseModule : CoworkeeModule;

builder.Services.AddCoworkeeReporting(baseAddress, reports => reports
    .DrillDown("SalesOrders", "sales/orders")
    .DrillDown("Products", "catalog/products"));
```

New apps get this with the template option `--reports`, which is on by default.

## Your OData entity sets as data sources

Reports use what you registered for OData, nothing else:

```csharp
services.AddODataEntity<SalesOrder>("SalesOrders", SalesPermissions.Orders.View);
services.AddODataEntity<Customer>("Customers", SalesPermissions.Orders.View);
```

Fields come from the EDM, so properties you hid from OData never appear, neither as a field nor in a filter. Navigation properties to another registered set add its fields, such as `Customer/Name`, as long as the user may read that set too. Labels come from `[Display(Name = ...)]` or `[ProvideFacet(Label = ...)]`, and enum members show their display names. Rows are the ones the OData list returns: tenant filter, soft delete and every row filter apply. A user without the permission for one set gets an error on the widgets that use it; the rest of the report still works.

## Aggregation in the database

Widgets send a query model, never an expression. The server checks every field against the source, builds `GroupBy` and `Select` with expression trees, and EF Core translates them to SQL on PostgreSQL, SQL Server and SQLite. The filter is an OData `$filter`, parsed against the EDM like on the OData endpoint.

```json
{
  "entitySet": "SalesOrders",
  "filter": "Region eq 'Nord' and Total gt 100",
  "groupBy": { "field": "OrderDate", "bucket": "Month" },
  "aggregates": [ { "function": "Sum", "field": "Total" }, { "function": "Count" } ],
  "sortBy": "Value", "descending": true, "top": 10
}
```

You can group by up to two fields, with dates bucketed by day, week, month, quarter or year, and ask for up to 8 values from `Count`, `Sum`, `Average`, `Min`, `Max` and `DistinctCount`. A query returns at most 5,000 groups.

Measured on the demo's 100,000 sales orders (Postgres, warm): count by status 9 ms, sum by month 29 ms, count by week 55 ms, region by channel 23 ms, top 10 customers with join 30 ms, four KPI values 58 ms.

## Widget types

| Widget | Shows |
|---|---|
| KPI | one value with the trend against the period before, a curve over time and a gauge towards a target |
| Chart | bar, horizontal bar, stacked bar, line, area, pie, donut, scatter, radar, treemap, heatmap |
| Table | rows of the set with endless scrolling, as table, cards or list |
| Data table | the full `CoworkeeDataTable` with paging, grouping, column chooser, facets and search |
| Pivot | categories as rows, series as columns, shaded by size, with totals |
| Text, image, web page | Markdown, an image or a sandboxed page |

Colours, font and dark mode come from the MudBlazor theme, so a custom theme needs no extra setup.

## A GridStack designer for the dashboard

![Report designer with the widget panel open](../assets/screenshots/reporting-designer.png){ .shot loading=lazy }

*Edit* switches the grid into edit mode: drag widgets by their title, resize them at the edges and add new ones. The layout uses GridStack with 12 columns and falls back to one column on phones. The widget panel edits data source, grouping, series, values, sort, top, date field, query builder rules, number format and target, and the widget redraws while you change it. Undo and redo cover every change until you save.

In view mode there is a date range for all widgets, facets per entity set, full screen per widget, a presentation mode and export to PNG, PDF, CSV and Excel.

## Cross filter and drill-down

Clicking a bar, a pie slice or a pivot cell filters the other widgets on the same entity set. Each group comes back from the server with an OData filter for exactly its rows, and that filter drives both the cross filter and *Open the data*: drill-down opens the list page registered with `DrillDown` (or a built-in data page), already filtered to the rows behind the number.

![A click on a pie slice filters the other widgets](../assets/screenshots/reporting-cross-filter.png){ .shot loading=lazy }

## Sharing and permissions

`Reports.View` opens reports shared with you, `Reports.Manage` creates and edits your own, `Reports.Share` shares them with roles or the organisation. Only the owner changes a report; everyone else duplicates it into a private copy. Changes to a report, and to the rows of `[Realtime]` entities, reach open dashboards over the realtime hub.

## Where to go next

- [Reporting](../modules/reporting.md): setup, query model, widgets, filters and endpoints
- [OData](../web/odata.md): registering entity sets, hidden properties and row filters
- [Data table](../ui/data-table.md): the table used by the data table widget
- [Query builder](../ui/query-builder.md): the rules editor of the widget panel
- [Realtime and notifications](../modules/realtime.md): how changes reach open dashboards
