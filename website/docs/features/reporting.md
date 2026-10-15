---
title: "Dashboards and reporting for Blazor apps"
description: "Self-service dashboards for Blazor and ASP.NET Core: a drag and drop designer, KPIs, charts and pivots over your OData sets, cross filters and SQL-side aggregation."
---

# Dashboards and reports

```csharp
[DependsOn(typeof(CoworkeeReportingModule))]
public sealed class MyAppDatabaseModule : CoworkeeModule;

builder.Services.AddCoworkeeReporting(baseAddress, reports => reports
    .DrillDown("SalesOrders", "sales/orders"));
```

That is the setup (plus one migration for `cw.Reports` and `cw.ReportFavorites`). New apps from the template have it already, `--reports` is on by default. Users get pages under `/reports` where they build dashboards from the entity sets you registered for OData:

```csharp
services.AddODataEntity<SalesOrder>("SalesOrders", SalesPermissions.Orders.View);
```

No second data model, no export job. Fields come from the EDM, so a property you hid from OData is not offered in a report either. Rows are the ones the OData list would return, with the tenant filter and every row filter applied, and a user who lacks the permission for one set sees an error on those widgets while the rest of the dashboard works.

![A sales dashboard with KPIs, charts, a pivot and a table](../assets/screenshots/reporting-dashboard.png){ .shot loading=lazy }

## Aggregated in SQL

A widget sends a small query model to `POST /api/v1/reports/query`, never an expression:

```json
{
  "entitySet": "SalesOrders",
  "filter": "Region eq 'Nord' and Total gt 100",
  "groupBy": { "field": "OrderDate", "bucket": "Month" },
  "aggregates": [ { "function": "Sum", "field": "Total" }, { "function": "Count" } ],
  "top": 10
}
```

The server checks each field against the source, builds `GroupBy` and `Select` as expression trees and leaves the SQL to EF Core, on PostgreSQL, SQL Server and SQLite alike. Up to two group fields (dates by day, week, month, quarter or year), up to 8 values from `Count`, `Sum`, `Average`, `Min`, `Max` and `DistinctCount`, at most 5,000 groups.

Measured on the demo's 100,000 sales orders (Postgres, warm): count by status 9 ms, sum by month 29 ms, count by week 55 ms, region by channel 23 ms, top 10 customers with join 30 ms, four KPI values 58 ms.

## Designer, cross filter and drill-down

![Report designer with the widget panel open](../assets/screenshots/reporting-designer.png){ .shot loading=lazy }

The grid is GridStack with 12 columns, one column on phones. In edit mode you drag widgets by the title, resize them at the edges and set up each one in the side panel; it redraws while you type. Undo covers everything until you save.

Widget types: KPI with trend and target, charts (bar, line, area, pie, donut, scatter, radar, treemap, heatmap and a few variants), a scrolling table, the full `CoworkeeDataTable`, a pivot, and Markdown, image or embedded page for the text around the numbers. Colours follow the MudBlazor theme, dark mode included.

Each group comes back from the server with an OData filter for exactly its rows. Click a bar or a pie slice and that filter narrows the other widgets on the same set; click *Open the data* and it lands on the page you registered with `DrillDown`, already filtered to the rows behind the number.

A report is private, shared with roles, or shared with the organisation (`Reports.Share`). Others can duplicate it, only the owner changes it, and a saved change reaches everyone who has it open. Export goes to PNG, PDF via the print dialog, or CSV and Excel per widget.

**Status:** reports have no version history, no public link and no map widget yet. On SQLite, date buckets on `DateTimeOffset` fields are rejected because EF stores them as text; `DateTime` and `DateOnly` work.

Reference: [Reporting](../modules/reporting.md), [OData](../web/odata.md), [Query builder](../ui/query-builder.md).
