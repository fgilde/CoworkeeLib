# Roadmap: parity of the demo app with the standalone template

Goal: the template app (`release/last-standalone`) looks and behaves as before, built on Coworkee, with better tables (facets), better permission UI and clean code (CCD, `.razor` + `.razor.cs`, few comments).

Decisions (2026-10-05):
- All former features return: localization, export/import, database backups, chat, AI assistant, extended attributes, SDKs.
- The AI assistant uses Coworkee's own MCP server; tools come from the app's requests automatically.
- Keycloak is an external login of the Coworkee auth server (login modes Internal / External / Both); users are provisioned locally on first sign-in.
- Missing pieces go into Nextended (facet applied-filter extractor, FluentValidation part, CodeGen fixes) and MudBlazor.Extensions where they fit.
- Order: A → B → C → D → E.

## A – Library foundation
1. UI replacement: a registry to replace any library component or page, layout slots, grouped navigation tree.
2. Request pipeline: typed behaviors per request type, performance warning, exception logging, caching.
3. HTTP: controllers and OData per entity (Nextended.Web `GenericODataController`, facets via `cn.facets`), response filters (Nextended.ResponseFilters), FluentValidation helpers.
4. Nextended: applied-filter extractor, `Applied` on facet responses, typed facet builder, CodeGen fixes (long inference, leading slash, diagnostics).

## B – Library UI
- `CoworkeeDataTable`: facet bar, filter chips, column filters, saved views, edit dialogs, multi-select, export/import, deep links.
- Layout parity: navigation tree with pin/mini mode, theme and language selection, about dialog.

## C – Template app
- Same look and behavior as before: home, dashboard, catalog (brands, products), documents, extended attributes, chat, assistant.
- Seeded default admin from the old constants; no setup wizard.
- Features/<Entity>/Commands|Queries, validators separate, OData controller for every entity.

## D – Keycloak
- External OIDC provider at the auth server, login modes, Aspire resource and client seeding.

## E – Documentation
- GitHub Pages site in German and English: modules, usings, extension points, screenshots taken by the E2E tests.
