---
title: "Use cases: what to build with a .NET app framework"
description: "Internal admin tools, multi-tenant B2B SaaS, customer portals and digital asset platforms built on Coworkee, the .NET application framework for Blazor and Aspire."
---

# What you can build with Coworkee

Coworkee is not tied to one industry. It is a base for applications where people sign in, work with records, share them with colleagues and need an administrator to keep things in order. Four kinds of apps show where it fits, what they take from the framework and what is left for you to write.

## Internal admin tool or back office

**The situation.** A team keeps product data, orders or contracts in spreadsheets and a shared drive. A small web app should replace that, with sign-in through the company's identity provider and a decent table view.

**What Coworkee covers.** Sign-in through [Keycloak or any OpenID Connect provider](../security/authentication.md), roles and [permissions per entity or per record](../security/permissions.md), a [data table](../ui/data-table.md) with facets, column filters, grouping, saved views and Excel export, edit dialogs generated from your request types, an [audit log on field level](../modules/auditing.md), and [database backups](../modules/backups.md) from the admin area.

**What you write.** The entities and their rules. The demo app does exactly this for a product catalog and sales orders: a few entity classes, the requests for create and update, an OData registration and a page per entity.

![Products grouped by brand with facets and column filters](../assets/screenshots/products-grouped-light.webp){ .shot loading=lazy }

## Multi-tenant B2B SaaS

**The situation.** You sell one application to many customer companies. Each company sees only its own data, has its own administrators and pays for a plan that decides which modules it gets.

**What Coworkee covers.** Organisations as tenants with their data separated in EF Core by a query filter, [features and editions](../modules/features.md) that switch modules on per tenant and carry limits like "reports per month", [settings](../modules/settings.md) a tenant administrator changes without touching yours, self registration with activation and email confirmation, [themes](../modules/theming.md) per organisation, and [Redis](../hosting/redis.md) when you run more than one instance.

**What you write.** Your product's domain, the feature definitions that map your pricing, and the onboarding text.

```csharp
public void Define(FeatureDefinitionContext context) =>
    context.Group("Reports", "Reports")
        .Add("Reports.Enabled", "Reports")
        .Add("Reports.MaxPerMonth", "Reports per month", FeatureType.Int, "10");
```

## Customer or partner portal

**The situation.** Customers should look up their orders, download documents and send questions without calling. The portal has to look like your brand, speak their language and work on a phone.

**What Coworkee covers.** A registration wizard with optional address, role choice and document upload, [three sign-in designs](../security/sign-in-pages.md) in your colors, [public files](../modules/files.md) with anonymous links, [notifications and chat](../modules/realtime.md), [mail templates and campaigns](../modules/mailing.md), and [23 shipped languages](../modules/localization.md) with machine translation for your own texts. Permissions on single resources let a customer see their own orders and nothing else.

**What you write.** The views on your data that customers may see, and the rules for who sees what.

![The sign-in page on a phone](../assets/screenshots/signin-phone-light.webp){ .shot loading=lazy }

## Digital asset and document platform

**The situation.** A company manages images, videos, documents and their metadata, with versions, search and fine-grained sharing.

**What Coworkee covers.** This is what [Sharemee](https://github.com/fgilde/sharemee) is built on: digital asset management, document management, a knowledge base and an intranet on one .NET 10 platform. It takes identity, permissions per item, auditing, realtime updates, settings, the admin UI, the REST API and the MCP endpoint from Coworkee and adds its own modules for assets, metadata, processing, search and workspaces.

**What you write.** The heavy domain parts, which is the point: Sharemee's own code is about assets, not about user administration.

## Apps with an AI assistant

Any of the apps above can switch on the [AI assistant](ai.md). It works through the same requests as the UI, with the permissions of the signed-in user, and asks before it changes data. Pick one of 14 providers, from Anthropic and OpenAI to a local Ollama model, and the app also serves an MCP endpoint so tools like Claude or Copilot can work with your data through personal API keys.

## When Coworkee is not the right fit

- A public website or a blog with no signed-in users. A static site generator is simpler.
- A single small API without a UI, admins or tenants. Plain ASP.NET Core minimal APIs do that with less.
- A team that has to stay on .NET 8 or an older runtime. Coworkee targets .NET 10.

## Where to go next

- [Installation](../getting-started/installation.md): run the demo app in a few minutes.
- [Command line](../getting-started/cli.md): `coworkee new` with the options for each of the cases above.
- [Why Coworkee](why-coworkee.md): what a framework saves compared with a hand-rolled project.
