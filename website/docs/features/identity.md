---
title: "Identity and permissions for ASP.NET Core apps"
description: "Users, roles, groups and permissions down to single resources, an OpenIddict auth server with BFF, Keycloak or any OIDC provider, API keys and data export."
---

# Identity, authentication and permissions for .NET business apps

Every business app needs the same security plumbing before it does anything useful: accounts, roles, a sign-in that works for the browser and for scripts, rules for who may register, a way to lock someone out right now, and an answer when a user asks for their data. Writing that yourself takes weeks, and the mistakes show up in an audit.

Coworkee ships it as packages. An OpenID Connect server built on OpenIddict signs users in, a backend for frontend keeps the tokens away from the browser, and a permission system checks every request on the server. Administrators manage users, roles, groups, clients and password rules in the app, without a deployment.

![Role editor with the permission matrix, grouped by module](../assets/screenshots/role-permissions.png){ .shot loading=lazy }

## Users, roles and groups

Administrators edit everything about a user on one page: names, contact data, language, active flag, email (with or without a confirmation mail), password or reset link, an invitation for users without a password, lockout, two-step verification reset, external sign-ins, roles, groups and the picture. New users are created in a dialog and either invited by mail or given an initial password.

Permissions are granted to roles, to groups or to a single user. Roles can be seeded in code, so a fresh installation starts with the roles your app needs:

```csharp
seed.Roles.Add(new SeedRole("Product Manager", "Maintains products",
    [CatalogPermissions.Products.View, CatalogPermissions.Products.Create, CatalogPermissions.Products.Edit]));
```

## Permissions down to a single resource

Each module defines its permissions with an `IPermissionDefinitionContributor`. The definitions build the permission matrix in the role editor: grouped, searchable, with a toggle per group. On the server a request declares what it needs, and the dispatcher checks it before the handler runs:

```csharp
[RequiresPermission(CatalogPermissions.Products.Delete)]
public sealed record DeleteProductsCommand(IReadOnlyList<Guid> Ids) : ICommand<Result>;
```

Inside a handler, `IPermissionChecker.IsGrantedAsync` answers the same question, and an overload takes a resource type and id for grants on one folder or one record. Grants inherit along a hierarchy you describe with `IResourceHierarchy`; `IResourceRestriction` takes access away again for certain roles. In Blazor, `PermissionGate`, navigation entries and table buttons hide what the user may not use. The UI only hides; the server decides.

## OpenIddict auth server and BFF

Three hosts share the work. The auth server (`Coworkee.AuthServer`) runs sign-in, registration, password reset, two-step verification and the account pages. The web host (`Coworkee.Bff`) does the OpenID Connect flow with PKCE, keeps the tokens in a server-side session store (Redis when configured) and gives the browser only a cookie with a session key. The API validates bearer tokens with `AddCoworkeeApiAuthentication`. The browser never sees an access token.

Administrators of the system organisation manage further OpenID Connect clients and scopes under *Administration › Applications* and *Scopes*: public or confidential, implicit or explicit consent, refresh tokens, and service clients that call APIs with `client_credentials`.

## Keycloak or any OpenID Connect provider

External sign-in is configuration, one section per provider:

```json
"Coworkee": {
  "Auth": {
    "External": {
      "Mode": "Both",
      "AutoProvision": true,
      "Providers": {
        "keycloak": { "DisplayName": "Keycloak", "Authority": "https://keycloak.example.com/realms/myapp", "ClientId": "myapp-auth", "ClientSecret": "..." }
      }
    }
  }
}
```

An external sign-in joins an existing account only when the provider marks the email as verified or is configured with `TrustEmail`. In development, `options.UseKeycloak()` in the Aspire app host starts Keycloak with a realm for the app, the same users and passwords as the app, and keeps the realm up to date on every start. Administrators switch between password, external providers or both at runtime with the setting `Auth.LoginMode`.

![Keycloak sign-in page for the realm of the app](../assets/screenshots/keycloak.png){ .shot loading=lazy }

The account pages come in three layouts (Card, Split, Slide) and follow the app's theme; [the sign-in pages](../security/sign-in-pages.md) describe them.

## Registration rules and password rules as settings

Self registration is a server-rendered wizard: account, personal data, an optional role choice and optional document uploads. Administrators decide at runtime whether registration is open (`Account.AllowRegistration`), whether new accounts wait for activation and whether the email must be confirmed first. Configuration adds allowed address patterns such as `*@example.com`, a required postal address and document slots with file types and size limits.

Password rules are settings too: length, unique characters, digits, upper and lower case, special characters, lockout after failed attempts, expiry in days and a history of up to 24 passwords. `SettingsPasswordValidator` enforces them for registration, reset, change, passwords set by an administrator and the setup wizard, and every password field shows the rules in force.

## Sessions you can end now

*Sign out everywhere* and *Lock* on a user's page take effect at once. Access tokens carry a hash of the security stamp; the API compares it on every request, Redis pub/sub tells the other API instances, open realtime connections get `SessionRevoked` and the BFF drops the cookie session. Changing the own password ends all other sessions and keeps the current one.

## Personal API keys

Users with `ApiKeys.Create` create keys for scripts and AI clients under *Account > API keys*, with an expiry of 7 to 365 days and permissions picked from their own. The key is shown once (`cwk_` plus 64 hex characters); the server stores only its SHA-256 hash. On every request the key's permissions are intersected with the current permissions of its user, so taking a role away from the user also takes it away from the key.

```bash
curl -H "X-Api-Key: cwk_..." https://api.myapp.example/odata/Products?$top=5
```

## Personal data export and account deletion

Each module that stores data about a user implements `IPersonalDataContributor`: a named JSON section for the export and an erase step. The account page has a *Privacy* tab to download `personal-data.json` or delete the account; administrators delete users from the user page. The last active administrator cannot be deleted.

## Where to go next

- [Authentication and Keycloak](../security/authentication.md): the three hosts, clients, external providers, setup wizard or seed
- [Permissions](../security/permissions.md): defining and checking permissions on the server and in Blazor
- [Registration and sign-in rules](../security/registration.md): the wizard, activation, documents and sign-in methods
- [Clients, sessions and password rules](../security/clients.md): OIDC clients, service clients, signing out everywhere
- [Sign-in pages and designs](../security/sign-in-pages.md): layouts, media and own designs
- [API keys](../security/api-keys.md): creating, using and auditing personal keys
- [Privacy and personal data](../security/privacy.md): built-in sections, export and erase endpoints
- [Aspire app host: Keycloak](../hosting/aspire.md#keycloak): the local Keycloak realm
