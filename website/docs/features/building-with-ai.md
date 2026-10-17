---
title: "Building .NET apps with AI agents on Coworkee"
description: "Why Coworkee is a good base for apps written with Claude Code, Copilot or Cursor: security, tenants, audit, realtime and the admin UI are packages, so the agent only writes the domain."
---

# Building with AI

A coding agent is good at domain code: an entity, a validator, a page, a report. It is less reliable at the parts that have to be right everywhere at once, such as checking a permission on every request, keeping one tenant's rows away from another, writing the audit trail in the same transaction or never sending an access token to the browser. Those parts are where a prompt that forgets one line leaves a hole.

In a Coworkee app they are not the agent's job. They come with the packages, they are covered by the library's tests, and the code the agent writes plugs into them. The agent writes the domain; the rest is already there.

## What the agent does not have to build

| Concern | Where it comes from |
|---|---|
| Sign-in, users, roles, groups, external providers | [Identity](identity.md): an OpenIddict auth server with Keycloak or any OpenID Connect provider; the browser never sees an access token, and the auth server and the web app send a Content-Security-Policy |
| Permissions | one `[RequiresPermission]` on a request; the [pipeline](../fundamentals/messaging.md) checks it for endpoints, jobs, the assistant and MCP alike |
| Tenants | `IMultiTenant` on an entity; the DbContext filters every query and fills the tenant on insert ([Multi-tenancy](multi-tenancy.md)) |
| Validation and transactions | FluentValidation and the unit of work run around every request; a handler does not call `SaveChangesAsync` |
| Audit, history and undo | field-level [audit trail](../modules/auditing.md) in the same transaction, record history with restore and per-field undo |
| Realtime | `[Realtime]` on an entity and open tables and detail pages reload when someone else changes it ([Realtime](realtime.md)) |
| Workflows and approvals | [states, transitions, approvals](../modules/workflows.md) with deadlines, reminders and escalation |
| Search, reporting, files | [global search](../web/global-search.md), [reports and dashboards](reporting.md) built in the browser, [file storage](../modules/files.md) with preview |
| Notifications, mail, i18n | the notification bell with push and a digest mail, mail templates, the UI in 23 [languages](localization.md) |
| Admin UI | users, roles, settings, themes, languages, translations, audit log, backups, API keys |
| MCP and API keys | every app is an [MCP server](../modules/mcp.md) under `/mcp`, authenticated with personal [API keys](../security/api-keys.md) |

The CLI generates entities on these defaults. Each entity of `coworkee entity add` or of a spec is an `AuditedEntity` with `IMultiTenant` and `[Realtime]`, with view, create, edit and delete permissions, a validator, endpoints, an OData set and a Blazor page ([Modules and entities](../getting-started/cli.md#modules-and-entities)). What the agent adds next to it starts from code that is already safe.

## One way to write a feature

Every use case is a request, a handler and a validator, sent through one dispatcher. An agent that has read one module has read all of them, and [Your first feature](../getting-started/first-feature.md) shows the pattern on a single entity. That consistency is what makes generated code reviewable: a missing permission attribute or a handler that saves on its own stands out in a diff.

```csharp
[RequiresPermission(CatalogPermissions.Brands.Delete)]
public sealed record DeleteBrandCommand(Guid Id) : ICommand<Result>;
```

## Tools for the agent

**Rules in the solution.** Every app comes with an `AGENTS.md` at its root, read by Codex, Copilot, Cursor and most other agents, and a `CLAUDE.md` that imports it for Claude Code. It names the solution layout, where entities, requests and pages go, the patterns every feature follows (`AuditedEntity`, `IMultiTenant`, `[Realtime]`, `[RequiresPermission]` with its permission definition, validators), the pitfalls below, and the commands for migrations, tests and the CLI. It follows the options of the app: without samples it does not point at the catalog, without tests it has no test section. Extend it with the rules of your domain.

**The CLI as an MCP server.** `coworkee mcp` gives Claude Code, Claude Desktop, VS Code and other clients the tools `list_options`, `get_spec_schema`, `get_spec_example`, `validate_spec`, `create_app`, `add_module`, `add_entity`, `import_entities`, `add_migration`, `add_option`, `run_doctor`, `update_packages` and `search_docs`. They run the same commands you would type. See [MCP server of the CLI](../getting-started/mcp.md).

```bash
claude mcp add coworkee -- coworkee mcp
```

**App specs.** A whole app, with options, branding, modules and entities, is one JSON file with a published [JSON Schema](../assets/spec.schema.json). An agent writes it, `coworkee spec validate` checks it, and `coworkee new --spec` creates the app. Coworkee Studio reads and writes the same file, so you can open what the agent planned and change it by hand. See [App specs](../getting-started/cli.md#app-specs) and the [starter specs](../getting-started/gallery.md).

**Predictable output.** `coworkee new` prints `==> <step>` before every step and exits with a non-zero code when one fails, so an agent can tell where it stopped.

**The running app as an MCP server.** Once the app runs, an agent can work with its data: `/mcp` lists only the tools the API key's owner may use, marks every command as destructive so the client asks first, and records each call in the AI tool call audit. See [MCP server of the app](../modules/mcp.md).

## What stays your job

Security that comes with the packages does not cover code that walks around them. Review what the agent wrote with these in mind:

- A request without `[RequiresPermission]` is not checked. Put the attribute on every request that is not meant for everyone.
- An entity without `IMultiTenant` is shared by all tenants.
- `ExecuteUpdate` and `ExecuteDelete` skip the change tracker: no audit entry, no realtime message.
- Records only some users may see need their own realtime topics.

`Coworkee.Testing` runs API tests against a real database, so a test the agent writes can prove that a user without the permission gets a 403 and another tenant sees nothing. See [Testing](../testing.md).
