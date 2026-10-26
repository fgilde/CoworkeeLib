---
title: "Identity and permissions for ASP.NET Core apps"
description: "Users, roles, groups and permissions down to single resources, an OpenIddict auth server with BFF, Keycloak or any OIDC provider, API keys and data export."
---

# Identity and permissions

Sign-in runs on its own host, an OpenID Connect server built on OpenIddict. The web host acts as backend for frontend: it does the code flow with PKCE, keeps the tokens in a server-side session and hands the browser a cookie. The API checks bearer tokens. The browser never holds an access token.

A new installation either seeds its tenant, administrator and roles from the migration service (the template does that) or asks for them in a setup wizard on first start, which is how Sharemee comes up.

![Role editor with the permission matrix, grouped by module](../assets/screenshots/role-permissions.png){ .shot loading=lazy }

## Who may do what

Permissions are strings that modules define. They are granted to roles, to groups or to one user, and the role editor shows them as a matrix with a toggle per group. A request declares what it needs with `[RequiresPermission]`, and the dispatcher checks it before the handler runs.

Grants can also target a single resource, one folder for example, and inherit down a hierarchy you describe with `IResourceHierarchy`. Blazor pages hide buttons and menu entries the user can't use, but that is cosmetic. The server decides.

Administrators edit everything about a user on one page, from the email address to lockout and two-step verification reset. *Sign out everywhere* and *Lock* act at once: tokens carry a hash of the security stamp, the API compares it on every request, and with Redis the other instances hear about it immediately.

## Group tree and scope

Groups form a tree per tenant, so they can stand for organisation units: customers, subsidiaries, branches. A group has an optional parent and a `Code`, a path of 5-digit segments (`00001.00003`) that only the handlers maintain. Create a group under a parent, move it (`PUT /api/v1/identity/groups/{id}/parent`, the subtree moves along) or delete it, which removes its sub-groups too. Names are unique among siblings, the depth is limited to 16. Groups of an existing app become roots; the migrations host gives them their codes on the next run.

Users are members of any number of groups. **Units grant nothing by themselves**: a group only decides what a user sees. Groups can still carry roles as before (Roles tab), but a parent's role is not inherited by its sub-groups. Members are added, removed or moved all at once to another group with `Identity.Groups.ManageMembers` (implied by `Identity.Groups.Manage`). `Identity.Groups.ScopeAll` ("Work in any group") makes every group of the tenant accessible, for operators who belong to no unit; administrators with all permissions have it. The tree endpoints (`/groups/children`, `/groups/tree`) accept `Groups.View` or `ScopeAll`.

On the groups page the tree loads lazily, with a search, counts, and tabs for General, Members and Roles. `?group={id}` deep-links to a group. Apps add their own tabs:

```csharp
services.AddGroupDetailTab<OrganisationCodes>("Organisation codes", ShipmentPermissions.Shipments.View);
// the component has [Parameter] public Guid GroupId { get; set; }
```

### Which groups a user sees

`IGroupScope` (namespace `Coworkee.Application.Groups`, no Identity reference needed) answers per request:

| Method | Result |
|---|---|
| `MembershipsAsync` | the groups the user belongs to directly |
| `AccessibleAsync` | memberships and all their sub-groups; every group of the tenant with `ScopeAll` |
| `SelectedAsync` | the user's selection, limited to what is accessible |
| `ActiveAsync` | the selection and its sub-groups; `AccessibleAsync` when nothing is selected |
| `NodesAsync`, `AncestorsAsync` | `GroupNode(Id, ParentId, Code, Name)` of groups or the path above one (root first) |

The picker in the app bar stores the selection per user on the server (`cw.GroupSelections`): nothing selected means everything the user may reach, some groups selected means those and their sub-groups. A selection never widens access, and a stale one (the user lost the membership) is ignored. It applies to interactive sign-ins only; personal API keys, MCP, service clients and background jobs see `AccessibleAsync`. Without a user every list is empty; bypasses (a "view all" permission) are the app's business.

Apps scope their rows with a filter written once against `ActiveAsync`:

```csharp
using Coworkee.Application.Groups;
using Coworkee.OData;

internal sealed class ShipmentFilter(IGroupScope scope) : IODataEntityFilter<Shipment>
{
    public async Task<IQueryable<Shipment>> ApplyAsync(IQueryable<Shipment> query, CancellationToken ct)
    {
        var ids = await scope.ActiveAsync(ct);
        return query.Where(s => EF.Parameter(ids).Contains(s.GroupId));
    }
}

// in the module
services.AddScoped<IODataEntityFilter<Shipment>, ShipmentFilter>();
```

`EF.Parameter` matters on SQL Server, which rejects more than 2100 parameters from a plain `Contains`.

The selection decides what **lists** show, not what a user may open. The library narrows collections, `$count`, facets, search, export, `QueryData`, client entities and reports by the selection. By-key reads, the guards of attachments and social features (`VisibleIdsAsync`, so tag link counts too), history, import matching, workflow visibility and webhooks run unnarrowed, so a link to a record of another unit still opens. Code of your own that means access, not listing, wraps in `using (GroupScope.Unnarrowed())`. Exports started in the UI capture the selection when requested and the job runs under `GroupScope.Selection(ids)`; if every captured group is stale by then, the file falls back to everything accessible. The client-entity mirror follows the selection (`IClientEntityPartitionPart`), so changing it resyncs the browser copy.

Place `<GroupScopePicker />` in your app bar yourself; the template layout does not include it, and it hides itself for users without accessible groups. Data tables reload when the selection changes (`GroupScopeState`), and `IClientDataCache` is the seam for your own client-side caches.

Group import (`Groups.Manage`) takes `Name`, `Description` and `ParentPath` ("Customer A/Hamburg") and is an upsert by path: a group of that name at that path gets its description updated, otherwise it is created. Missing parents are not created implicitly (an earlier row of the same file may create them); an unknown parent is an error row. A `/` in a name cannot be addressed by path, empty path segments are ignored, a blank description does not clear an existing one, and a dry run cannot flag unknown paths (a real run does). The Users OData set exposes `Groups` for filters such as `Groups/any(m: m/GroupId eq {id})`.

!!! warning "Realtime and units as customers"
    `[Realtime]` still broadcasts `type:X` to everyone with the view permission. When units are customers in one tenant, clients of other units see the ids and changed property names of records they may not open (and reload). Per-group topics are a follow-up.

Workflow approvers, mail to a group, resource permissions and group roles use **direct** membership, not the tree.

## Keycloak and other providers

Any OpenID Connect provider can sign users in; each one is a section under `Coworkee:Auth:External:Providers`. An external login joins an existing account only when the provider marks the email as verified (or is configured with `TrustEmail`). In development, `options.UseKeycloak()` in the Aspire app host starts Keycloak with a realm that has the same users and passwords as the app.

Whether people sign in with a password, a provider or both is the setting `Auth.LoginMode`, changeable at runtime. Self registration (off unless `Account.AllowRegistration` is set) can require activation by an administrator, a confirmed address, allowed address patterns and uploaded documents. Password rules are settings too, up to a history of 24 passwords, and every password field lists the rules in force.

## Keys and personal data

Scripts and AI clients use personal API keys: `cwk_` plus 64 hex characters, shown once, stored as a SHA-256 hash, valid for 7 to 365 days. A key never has more than its user has right now; take a role away from the user and the key loses it on the next request.

Modules that store data about a person implement `IPersonalDataContributor`. The *Privacy* tab of the account page then downloads everything as `personal-data.json`, or deletes the account.

**Status:** version 0.0.1.x, so names in this area can still change between releases; the [changelog](../reference/changelog.md) lists what to adapt.

Reference: [authentication and Keycloak](../security/authentication.md), [permissions](../security/permissions.md), [registration and sign-in rules](../security/registration.md), [clients, sessions and password rules](../security/clients.md), [sign-in pages](../security/sign-in-pages.md), [API keys](../security/api-keys.md), [privacy](../security/privacy.md).
