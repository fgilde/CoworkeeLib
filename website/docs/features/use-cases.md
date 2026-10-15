---
title: "Use cases: what to build with a .NET app framework"
description: "Back office tools, multi-tenant B2B SaaS, customer portals and a digital asset platform built on Coworkee, the .NET application framework for Blazor and Aspire."
---

# Use cases

## Sharemee, the asset platform

Sharemee is the second app I built on Coworkee, after the demo. It is a platform for digital assets and documents, with a knowledge base and an intranet on top, and it is still early.

What it takes from the library: identity, permissions per item, the audit trail, realtime updates, settings, the admin pages, the REST API and the MCP endpoint. Its own projects are called Assets, Metadata, Processing, Search and Workspaces, and that is roughly what they contain. When I fix something in the sign-in flow, Sharemee gets it with the next package update.

## A back office for orders and products

This is what the demo app is. A product catalog, documents and 100,000 sales orders, with a [data table](../ui/data-table.md) per entity: facets, column filters, grouping, saved views, Excel export. Edit dialogs come from the request types, every field change lands in the [audit log](../modules/auditing.md), and an administrator can take a [database backup](../modules/backups.md) from the admin area.

![Products grouped by brand with facets and column filters](../assets/screenshots/products-grouped-light.webp){ .shot loading=lazy }

The demo's own code is a few entity classes, the create and update requests, one OData registration per set and a page per entity. Sign-in can go through the company's Keycloak or any other [OpenID Connect provider](../security/authentication.md).

## Multi-tenant B2B SaaS

One app, many customer companies. Each organisation sees only its own rows (EF Core adds the tenant filter to every query), has its own administrators and gets the modules its plan includes. Plans are [editions](../modules/features.md), and a feature can carry a limit as well as an on/off switch:

```csharp
public void Define(FeatureDefinitionContext context) =>
    context.Group("Reports", "Reports")
        .Add("Reports.Enabled", "Reports")
        .Add("Reports.MaxPerMonth", "Reports per month", FeatureType.Int, "10");
```

Tenant administrators change their [settings](../modules/settings.md) without touching yours. Run more than one instance and [Redis](../hosting/redis.md) keeps cache and realtime in step.

## A customer portal

Customers register through a wizard (address, role and document upload are optional steps), sign in on a page in your colors and see their own orders, because [permissions](../security/permissions.md) can be granted on a single record. [Public files](../modules/files.md) get anonymous links for the things everyone may download.

![The sign-in page on a phone](../assets/screenshots/signin-phone-light.webp){ .shot loading=lazy }

Most portals need more than one language. The packages ship 23, mails and sign-in pages included, and your own texts can be machine translated (then checked by someone who speaks the language, ideally).

## Any of the above, with an assistant

The [AI assistant](ai.md) works through the same requests as the UI, with the signed-in user's permissions. Every change to data waits until the user clicks *Run*.

## Not a good fit

- A public website or blog. Nobody signs in, so most of the framework would sit idle.
- One small API with no UI and no admins. Minimal APIs are less work.
- Anything that has to stay on .NET 8. Coworkee needs .NET 10.

To try one of these, [install the CLI](../getting-started/installation.md) and run `coworkee new`.
